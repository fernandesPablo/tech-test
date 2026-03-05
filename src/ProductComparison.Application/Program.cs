using ProductComparison.CrossCutting.Middleware;
using ProductComparison.Infrastructure.IoC;
using System.Reflection;
using System.Threading.RateLimiting;
using Serilog;
using ProductComparison.Infrastructure.BackgroundServices;
using Microsoft.AspNetCore.Hosting.Server.Features;
using StackExchange.Redis;
using ProductComparison.Infrastructure.Configuration;

// Configure Serilog from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true, reloadOnChange: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .CreateLogger();

try
{
    Log.Information("Starting Product Comparison API");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

    // Configure CSV Backup Service
    builder.Services.AddOptions<CsvBackupOptions>()
        .Bind(builder.Configuration.GetSection(CsvBackupOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddHostedService<CsvBackupService>();

    // Add services to the container.
    builder.Services.AddControllers()
        .ConfigureApiBehaviorOptions(options =>
        {
            options.SuppressModelStateInvalidFilter = false; // Ensure automatic validation
        });

    // Register Redis ConnectionMultiplexer for advanced operations (pattern-based deletion)
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var redisConnectionString = builder.Configuration.GetConnectionString("RedisConnection");
        return ConnectionMultiplexer.Connect(redisConnectionString ?? "localhost:6379");
    });

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        // Obtém a string de conexão do arquivo appsettings.json
        options.Configuration = builder.Configuration.GetConnectionString("RedisConnection");

        // Define um prefixo para as chaves
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "ProdComparison:";
    });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new()
        {
            Title = "Product Comparison API",
            Version = "v1",
            Description = "An API for product comparison.",
            Contact = new()
            {
                Name = "Product Comparison Team"
            }
        });

        // Include XML comments
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        c.IncludeXmlComments(xmlPath);
    });

    // Configure CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    builder.Services.AddOptions<RateLimitingOptions>()
        .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    var rateLimitOptions = builder.Configuration
        .GetSection(RateLimitingOptions.SectionName)
        .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

    builder.Services.AddRateLimiter(options =>
    {
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ipAddress,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitLimit,
                    Window = TimeSpan.FromMinutes(rateLimitOptions.WindowMinutes),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = rateLimitOptions.QueueLimit
                });
        });

        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            var retryAfterSeconds = rateLimitOptions.RetryAfterSeconds;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                retryAfterSeconds = (int)retryAfter.TotalSeconds;
                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            }

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                statusCode = 429,
                message = "Too many requests. Please try again later.",
                retryAfterSeconds
            }, cancellationToken);
        };
    });

    // Register all services
    NativeInjector.RegisterServices(builder.Services, builder.Configuration);

    var app = builder.Build();

    // Global error handling
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Enable Rate Limiting (must be before CORS and endpoints)
    app.UseRateLimiter();

    // Enable CORS
    app.UseCors("AllowAll");

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        // Em desenvolvimento, não forçamos HTTPS
    }
    else
    {
        // Em produção, usamos HTTPS
        app.UseHttpsRedirection();
    }

    app.UseAuthorization();
    app.MapControllers();

    // Map health check endpoints
    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            var result = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                checks = report.Entries.Select(e => new
                {
                    name = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    data = e.Value.Data,
                    duration = e.Value.Duration.TotalMilliseconds
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds
            });
            await context.Response.WriteAsync(result);
        }
    });

    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("database")
    });

    app.MapHealthChecks("/health/live");

    var lifetime = app.Lifetime;

    lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;

        if (addresses is { Count: > 0 })
        {
            Log.Information("🚀 Application started and listening on:");

            foreach (var address in addresses)
            {
                Log.Information("   → {Address}", address);
                Log.Information("     📖 Swagger: {SwaggerUrl}/swagger", address);
                Log.Information("     ❤️ Health: {HealthUrl}/health/live", address);
            }
        }
        else
        {
            Log.Warning("⚠️ Application started but no listening addresses were detected.");
        }
    });


    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }

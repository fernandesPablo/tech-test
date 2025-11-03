using ProductComparison.CrossCutting.Middleware;
using ProductComparison.Infrastructure.IoC;
using System.Reflection;
using System.Threading.RateLimiting;
using Serilog;
using StackExchange.Redis;
using ProductComparison.Infrastructure.BackgroundServices;

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
    builder.Services.Configure<CsvBackupOptions>(
        builder.Configuration.GetSection("CsvBackup"));

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
        var configuration = builder.Configuration.GetConnectionString("RedisConnection");
        return ConnectionMultiplexer.Connect(configuration ?? "localhost:6379");
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

    // Configure Rate Limiting
    var rateLimitConfig = builder.Configuration.GetSection("RateLimiting");
    var permitLimit = rateLimitConfig.GetValue<int>("PermitLimit", 100);
    var windowMinutes = rateLimitConfig.GetValue<int>("WindowMinutes", 1);
    var queueLimit = rateLimitConfig.GetValue<int>("QueueLimit", 10);
    var retryAfterSecondsDefault = rateLimitConfig.GetValue<int>("RetryAfterSeconds", 60);

    builder.Services.AddRateLimiter(options =>
    {
        // Global rate limit: configured requests per configured window per IP
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ipAddress,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(windowMinutes),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = queueLimit
                });
        });

        // Reject request handler
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            var retryAfterSeconds = retryAfterSecondsDefault;
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

    // Log application URLs before starting
    var urls = app.Urls.Any() ? app.Urls : new[] { "http://localhost:5000" };
    Log.Information("🚀 Application starting on: {Urls}", string.Join(", ", urls));
    Log.Information("📖 Swagger UI available at: {SwaggerUrl}/swagger", urls.First());
    Log.Information("❤️ Health check available at: {HealthUrl}/health", urls.First());

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

// Expõe Program class para testes de integração
public partial class Program { }

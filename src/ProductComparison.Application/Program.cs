using ProductComparison.CrossCutting.Middleware;
using ProductComparison.Infrastructure.IoC;
using System.Reflection;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Events;
using StackExchange.Redis;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/product-comparison-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Product Comparison API");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

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
        options.InstanceName = "ProdComparison:";
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
    builder.Services.AddRateLimiter(options =>
    {
        // Global rate limit: 100 requests per minute per IP
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: ipAddress,
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10
                });
        });

        // Reject request handler
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            var retryAfterSeconds = 60;
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

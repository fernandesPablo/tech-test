using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ProductComparison.CrossCutting.Middleware;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Domain.Services;
using ProductComparison.Infrastructure.Configuration;
using ProductComparison.Infrastructure.HealthChecks;
using ProductComparison.Infrastructure.Repositories;
using ProductComparison.Infrastructure.Caching;

namespace ProductComparison.Infrastructure.IoC;

public static class NativeInjector
{
    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Configure Repository
        var repositoryConfig = new RepositoryConfiguration();
        configuration.GetSection(nameof(RepositoryConfiguration)).Bind(repositoryConfig);

        // Se CsvFilePath não foi configurado, usa valores default
        if (string.IsNullOrWhiteSpace(repositoryConfig.CsvFilePath))
        {
            if (string.IsNullOrWhiteSpace(repositoryConfig.BaseDirectory))
            {
                repositoryConfig.BaseDirectory = "";
            }
            if (string.IsNullOrWhiteSpace(repositoryConfig.CsvFolder))
            {
                repositoryConfig.CsvFolder = "Csv";
            }
            if (string.IsNullOrWhiteSpace(repositoryConfig.ProductsFileName))
            {
                repositoryConfig.ProductsFileName = "products.csv";
            }
        }

        // Register configurations
        services.AddSingleton(repositoryConfig);

        // Register repositories and services
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductAuditLogRepository, ProductAuditLogRepository>();
        services.AddScoped<IProductService, ProductService>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // Register middleware
        services.AddTransient<ExceptionHandlingMiddleware>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<CsvFileHealthCheck>(
                name: "csv_database",
                tags: new[] { "database", "storage" })
            .AddCheck<RedisHealthCheck>(
                name: "redis_cache",
                tags: new[] { "cache", "memory" });
    }
}
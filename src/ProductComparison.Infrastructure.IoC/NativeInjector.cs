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
        var repositoryConfig = configuration
            .GetSection(nameof(RepositoryConfiguration))
            .Get<RepositoryConfiguration>();

        if (repositoryConfig != null)
        {
            // Se o caminho for relativo, resolve em relação à pasta do projeto
            if (!Path.IsPathRooted(repositoryConfig.BaseDirectory))
            {
                var projectPath = Directory.GetCurrentDirectory();
                repositoryConfig.BaseDirectory = Path.GetFullPath(Path.Combine(projectPath, "..", repositoryConfig.BaseDirectory));
            }
        }
        else
        {
            repositoryConfig = new RepositoryConfiguration
            {
                BaseDirectory = ".",
                CsvFolder = "Csv",
                ProductsFileName = "products.csv"
            };
        }

        // Register configurations
        services.AddSingleton(repositoryConfig);

        // Register repositories and services
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductService, ProductService>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // Register middleware
        services.AddTransient<ExceptionHandlingMiddleware>();

        // Register health checks
        services.AddHealthChecks()
            .AddCheck<CsvFileHealthCheck>(
                name: "csv_database",
                tags: new[] { "database", "storage" });
    }
}
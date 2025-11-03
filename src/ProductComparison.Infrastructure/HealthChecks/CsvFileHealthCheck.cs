using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ProductComparison.Infrastructure.Configuration;
using ProductComparison.Infrastructure.Utilities;

namespace ProductComparison.Infrastructure.HealthChecks;

public class CsvFileHealthCheck : IHealthCheck
{
    private readonly string _csvFilePath;
    private readonly ILogger<CsvFileHealthCheck>? _logger;

    public CsvFileHealthCheck(RepositoryConfiguration configuration, ILogger<CsvFileHealthCheck>? logger = null)
    {
        _logger = logger;

        _logger?.LogWarning("=== CSV HEALTH CHECK INIT ===");
        _logger?.LogWarning("CsvFilePath from config: '{Path}'", configuration.CsvFilePath ?? "NULL");
        _logger?.LogWarning("BaseDirectory: '{Dir}'", configuration.BaseDirectory);
        _logger?.LogWarning("CsvFolder: '{Folder}'", configuration.CsvFolder);
        _logger?.LogWarning("ProductsFileName: '{File}'", configuration.ProductsFileName);

        // Usa CsvPathResolver para centralizar a lógica de resolução de caminho
        _csvFilePath = CsvPathResolver.ResolvePath(configuration);
        _logger?.LogWarning("Resolved CSV path: {Path}", _csvFilePath);

        _logger?.LogWarning("=== FINAL CSV PATH: {Path} ===", _csvFilePath);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if file exists
            if (!File.Exists(_csvFilePath))
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy(
                        $"CSV database file not found at: {_csvFilePath}"));
            }

            // Check if file is readable
            using var stream = File.OpenRead(_csvFilePath);

            // Check if file has content (at least header)
            var fileInfo = new FileInfo(_csvFilePath);
            if (fileInfo.Length == 0)
            {
                return Task.FromResult(
                    HealthCheckResult.Degraded(
                        "CSV database file exists but is empty",
                        data: new Dictionary<string, object>
                        {
                            { "filePath", _csvFilePath },
                            { "fileSize", fileInfo.Length }
                        }));
            }

            return Task.FromResult(
                HealthCheckResult.Healthy(
                    "CSV database file is accessible",
                    data: new Dictionary<string, object>
                    {
                        { "filePath", _csvFilePath },
                        { "fileSize", fileInfo.Length },
                        { "lastModified", fileInfo.LastWriteTimeUtc }
                    }));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "CSV database file access denied",
                    ex));
        }
        catch (IOException ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "CSV database file I/O error",
                    ex));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "CSV database health check failed",
                    ex));
        }
    }
}

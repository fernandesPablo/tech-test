using Microsoft.Extensions.Diagnostics.HealthChecks;
using ProductComparison.Infrastructure.Configuration;

namespace ProductComparison.Infrastructure.HealthChecks;

public class CsvFileHealthCheck : IHealthCheck
{
    private readonly string _csvFilePath;

    public CsvFileHealthCheck(RepositoryConfiguration configuration)
    {
        // Usa CsvFilePath se especificado (testes), senão constrói o caminho
        if (!string.IsNullOrWhiteSpace(configuration.CsvFilePath))
        {
            _csvFilePath = configuration.CsvFilePath;
        }
        else
        {
            var baseDir = configuration.BaseDirectory?.TrimEnd('\\', '/') ?? string.Empty;
            _csvFilePath = Path.Combine(baseDir, configuration.CsvFolder, configuration.ProductsFileName);
            _csvFilePath = Path.GetFullPath(_csvFilePath);
        }
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

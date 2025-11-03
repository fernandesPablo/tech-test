using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductComparison.Infrastructure.Configuration;

namespace ProductComparison.Infrastructure.BackgroundServices;

public class CsvBackupService : BackgroundService
{
    private readonly ILogger<CsvBackupService> _logger;
    private readonly string _csvFilePath;
    private readonly string _backupDirectory;
    private readonly TimeSpan _backupInterval;
    private readonly int _maxBackups;

    public CsvBackupService(
        RepositoryConfiguration repositoryConfig,
        IOptions<CsvBackupOptions> options,
        ILogger<CsvBackupService> logger)
    {
        _logger = logger;
        
        // Usa a mesma lógica do ProductRepository para construir o caminho
        if (!string.IsNullOrWhiteSpace(repositoryConfig.CsvFilePath))
        {
            _csvFilePath = repositoryConfig.CsvFilePath;
        }
        else
        {
            var baseDir = repositoryConfig.BaseDirectory?.TrimEnd('\\', '/');
            
            // Se baseDir for "." ou vazio, usa o diretório onde está o executável
            // e volta 3 níveis (de bin/Debug/net9.0 para a raiz do projeto)
            if (string.IsNullOrWhiteSpace(baseDir) || baseDir == ".")
            {
                baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
            }
            
            _csvFilePath = Path.Combine(baseDir, repositoryConfig.CsvFolder, repositoryConfig.ProductsFileName);
            _csvFilePath = Path.GetFullPath(_csvFilePath);
        }
        
        // Backup directory fica na mesma pasta do CSV
        var csvDirectory = Path.GetDirectoryName(_csvFilePath) ?? string.Empty;
        _backupDirectory = Path.Combine(csvDirectory, "Backups");
        
        _backupInterval = TimeSpan.FromMinutes(options.Value.BackupIntervalMinutes);
        _maxBackups = options.Value.MaxBackups;

        _logger.LogInformation("CSV Backup Service configured - CSV Path: {CsvPath}, Backup Directory: {BackupDir}", 
            _csvFilePath, _backupDirectory);

        EnsureBackupDirectoryExists();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CSV Backup Service started");

        // Check integrity on startup
        await CheckAndRecoverAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_backupInterval, stoppingToken);

                await CreateBackupAsync();
                await CleanOldBackupsAsync();
                await CheckAndRecoverAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CSV backup service");
            }
        }

        _logger.LogInformation("CSV Backup Service stopped");
    }

    private async Task CreateBackupAsync()
    {
        if (!File.Exists(_csvFilePath))
        {
            _logger.LogWarning("CSV file not found at {Path}, skipping backup", _csvFilePath);
            return;
        }

        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"products_backup_{timestamp}.csv";
            var backupFilePath = Path.Combine(_backupDirectory, backupFileName);

            await Task.Run(() => File.Copy(_csvFilePath, backupFilePath, overwrite: true));

            _logger.LogInformation("Created backup: {BackupFile}", backupFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup");
        }
    }

    private async Task CheckAndRecoverAsync()
    {
        // Check if file exists
        if (!File.Exists(_csvFilePath))
        {
            _logger.LogWarning("CSV file missing, attempting recovery from backup");
            await RecoverFromBackupAsync();
            return;
        }

        // Check if file is valid (can be parsed)
        try
        {
            var lines = await File.ReadAllLinesAsync(_csvFilePath);
            if (lines.Length == 0 || !lines[0].Contains("Id,Name"))
            {
                _logger.LogWarning("CSV file corrupted (invalid header), attempting recovery");
                await RecoverFromBackupAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV file corrupted, attempting recovery");
            await RecoverFromBackupAsync();
        }
    }

    private async Task RecoverFromBackupAsync()
    {
        var latestBackup = GetLatestBackup();

        if (latestBackup == null)
        {
            _logger.LogError("No backup available for recovery");
            return;
        }

        try
        {
            await Task.Run(() => File.Copy(latestBackup, _csvFilePath, overwrite: true));
            _logger.LogInformation("Successfully recovered CSV from backup: {Backup}", Path.GetFileName(latestBackup));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover from backup");
        }
    }

    private string? GetLatestBackup()
    {
        var backups = Directory.GetFiles(_backupDirectory, "products_backup_*.csv")
                              .OrderByDescending(f => f)
                              .ToList();

        return backups.FirstOrDefault();
    }

    private Task CleanOldBackupsAsync()
    {
        try
        {
            var backups = Directory.GetFiles(_backupDirectory, "products_backup_*.csv")
                                  .OrderByDescending(f => f)
                                  .ToList();

            if (backups.Count <= _maxBackups)
                return Task.CompletedTask;

            var backupsToDelete = backups.Skip(_maxBackups);

            foreach (var backup in backupsToDelete)
            {
                File.Delete(backup);
                _logger.LogDebug("Deleted old backup: {Backup}", Path.GetFileName(backup));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean old backups");
        }

        return Task.CompletedTask;
    }

    private void EnsureBackupDirectoryExists()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            Directory.CreateDirectory(_backupDirectory);
            _logger.LogInformation("Created backup directory: {Directory}", _backupDirectory);
        }
    }
}
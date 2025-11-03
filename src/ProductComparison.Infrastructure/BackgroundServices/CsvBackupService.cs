using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductComparison.Infrastructure.Configuration;
using ProductComparison.Infrastructure.Utilities;

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

        // Usa CsvPathResolver para centralizar a lógica de resolução de caminho
        _csvFilePath = CsvPathResolver.ResolvePath(repositoryConfig);

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

        // Aguarda 30 segundos para evitar race condition com ProductRepository no startup
        _logger.LogInformation("Waiting 30 seconds before first integrity check to allow repository initialization");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

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

            // Validação 1: Arquivo vazio
            if (lines.Length == 0)
            {
                _logger.LogWarning("CSV file is empty, attempting recovery");
                await RecoverFromBackupAsync();
                return;
            }

            // Validação 2: Header exato (deve ter exatamente 10 campos)
            var expectedHeader = "Id,Name,Description,ImageUrl,Price,Rating,Brand,Color,Weight,Version";
            if (lines[0] != expectedHeader)
            {
                _logger.LogWarning("CSV file has invalid header. Expected: '{Expected}', Found: '{Found}'",
                    expectedHeader, lines[0]);
                await RecoverFromBackupAsync();
                return;
            }

            // Validação 3: Verifica se pelo menos 3 linhas de dados têm 10 campos
            // (evita falso positivo se o arquivo estiver sendo escrito)
            var dataLines = lines.Skip(1).Take(3).ToList();
            if (dataLines.Any())
            {
                foreach (var line in dataLines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var fields = line.Split(',');
                    if (fields.Length != 10)
                    {
                        _logger.LogWarning("CSV file has data line with {FieldCount} fields instead of 10, attempting recovery",
                            fields.Length);
                        await RecoverFromBackupAsync();
                        return;
                    }
                }
            }

            _logger.LogDebug("CSV file integrity check passed");
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            // Arquivo está sendo usado - isso é NORMAL, não tenta recuperar
            _logger.LogDebug("CSV file is being used by another process, skipping integrity check");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV file corrupted or unreadable, attempting recovery");
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

        // Retry logic para evitar conflitos com ProductRepository
        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Aguarda um pouco antes de tentar (aumenta chance de ProductRepository liberar o arquivo)
                if (attempt > 1)
                {
                    var delayMs = attempt * 500; // 500ms, 1000ms, 1500ms
                    _logger.LogInformation("Retry {Attempt}/{MaxRetries} - waiting {Delay}ms before recovery attempt",
                        attempt, maxRetries, delayMs);
                    await Task.Delay(delayMs);
                }

                await Task.Run(() => File.Copy(latestBackup, _csvFilePath, overwrite: true));
                _logger.LogInformation("Successfully recovered CSV from backup: {Backup}", Path.GetFileName(latestBackup));
                return; // Sucesso, sai da função
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process") && attempt < maxRetries)
            {
                _logger.LogWarning("CSV file is locked (attempt {Attempt}/{MaxRetries}), will retry", attempt, maxRetries);
                // Continua para próxima tentativa
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover from backup (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                if (attempt == maxRetries)
                {
                    _logger.LogError("All recovery attempts failed, giving up");
                }

                return; // Erro não recuperável, sai
            }
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
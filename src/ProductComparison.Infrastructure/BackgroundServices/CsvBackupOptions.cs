namespace ProductComparison.Infrastructure.BackgroundServices;

public class CsvBackupOptions
{
    public int BackupIntervalMinutes { get; set; } = 30; // Backup every 30 minutes
    public int MaxBackups { get; set; } = 10; // Keep last 10 backups
}
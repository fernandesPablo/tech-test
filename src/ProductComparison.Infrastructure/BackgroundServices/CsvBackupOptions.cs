namespace ProductComparison.Infrastructure.BackgroundServices;

/// <summary>
/// Configurações do serviço de backup de CSV.
/// O caminho do CSV é resolvido via RepositoryConfiguration + CsvPathResolver.
/// </summary>
public class CsvBackupOptions
{
    /// <summary>
    /// Intervalo entre backups automáticos (em minutos). Padrão: 30 minutos.
    /// </summary>
    public int BackupIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Número máximo de backups a manter. Backups mais antigos são removidos. Padrão: 10.
    /// </summary>
    public int MaxBackups { get; set; } = 10;
}
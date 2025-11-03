namespace ProductComparison.Infrastructure.Utilities;

using ProductComparison.Infrastructure.Configuration;

/// <summary>
/// Centraliza a lógica de resolução do caminho do arquivo CSV.
/// Elimina duplicação de código entre ProductRepository, CsvBackupService e CsvFileHealthCheck.
/// </summary>
public static class CsvPathResolver
{
    /// <summary>
    /// Resolve o caminho completo do arquivo CSV baseado na configuração.
    /// </summary>
    /// <param name="configuration">Configuração do repositório</param>
    /// <returns>Caminho absoluto do arquivo CSV</returns>
    public static string ResolvePath(RepositoryConfiguration configuration)
    {
        // Se CsvFilePath está definido, usa ele diretamente (para testes)
        if (!string.IsNullOrWhiteSpace(configuration.CsvFilePath))
        {
            return configuration.CsvFilePath;
        }

        // Caso contrário, constrói o caminho a partir das partes
        var baseDir = configuration.BaseDirectory?.TrimEnd('\\', '/');

        // Se baseDir for "." ou vazio, usa o diretório onde está o executável
        // e volta 3 níveis (de bin/Debug/net9.0 para a raiz do projeto)
        if (string.IsNullOrWhiteSpace(baseDir) || baseDir == ".")
        {
            baseDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        }

        var csvFilePath = Path.Combine(baseDir, configuration.CsvFolder, configuration.ProductsFileName);
        return Path.GetFullPath(csvFilePath);
    }
}

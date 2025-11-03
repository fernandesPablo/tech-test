namespace ProductComparison.Infrastructure.Configuration;

public class RepositoryConfiguration
{
    public string BaseDirectory { get; set; } = string.Empty;
    public string CsvFolder { get; set; } = "Csv";
    public string ProductsFileName { get; set; } = "products.csv";

    /// <summary>
    /// Caminho direto para o arquivo CSV (sobrescreve BaseDirectory/CsvFolder/ProductsFileName se preenchido)
    /// </summary>
    public string? CsvFilePath { get; set; }
}
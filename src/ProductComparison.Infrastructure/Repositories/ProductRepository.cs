using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Infrastructure.Configuration;
using ProductComparison.Infrastructure.Utilities;
using System.Globalization;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ProductComparison.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly string _csvFilePath;
    private readonly string _header = "Id,Name,Description,ImageUrl,Price,Rating,Brand,Color,Weight,Version";
    private readonly ILogger<ProductRepository> _logger;

    public ProductRepository(RepositoryConfiguration configuration, ILogger<ProductRepository> logger)
    {
        _logger = logger;

        // Usa CsvPathResolver para centralizar a lógica de resolução de caminho
        _csvFilePath = CsvPathResolver.ResolvePath(configuration);
        _logger.LogInformation("[ProductRepository] Resolved CSV path: {Path}", _csvFilePath);
        EnsureFileExists();
    }

    private void EnsureFileExists()
    {
        var directory = Path.GetDirectoryName(_csvFilePath);

        try
        {
            // CreateDirectory é idempotente (não falha se já existe)
            Directory.CreateDirectory(directory!);

            // Usa FileMode.CreateNew para prevenir sobrescrita
            if (!File.Exists(_csvFilePath))
            {
                try
                {
                    using var fs = new FileStream(_csvFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(fs);
                    writer.WriteLine(_header);
                }
                catch (IOException) when (File.Exists(_csvFilePath))
                {
                    _logger.LogDebug("CSV file already created by another thread");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure CSV file exists");
            throw;
        }
    }

    /// <summary>
    /// Opens the CSV file for reading with shared lock (allows concurrent reads and writes).
    /// </summary>
    private FileStream OpenFileForReading()
    {
        EnsureFileExists();
        return new FileStream(
            _csvFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
    }

    /// <summary>
    /// Opens the CSV file for writing with exclusive lock (no other process can read or write).
    /// </summary>
    private FileStream OpenFileForWriting()
    {
        EnsureFileExists();
        return new FileStream(
            _csvFilePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    /// <summary>
    /// Executes an operation with automatic retry logic for handling file lock contention.
    /// </summary>
    private async Task<T> ExecuteWithRetry<T>(
        Func<Task<T>> operation,
        string operationName,
        bool shouldRetryOnConcurrencyException = true)
    {
        const int maxRetries = 5;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyException) when (!shouldRetryOnConcurrencyException)
            {
                // Don't retry on concurrency conflicts if specified
                throw;
            }
            catch (IOException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                _logger.LogDebug("File locked during {Operation}, retrying... ({Attempt}/{Max})",
                    operationName, retryCount, maxRetries);
                await Task.Delay(50 * retryCount); // Exponential backoff
            }
        }

        throw new IOException($"Failed to {operationName} after {maxRetries} retries due to file lock contention");
    }

    /// <summary>
    /// Executes a void operation with automatic retry logic for handling file lock contention.
    /// </summary>
    private async Task ExecuteWithRetry(
        Func<Task> operation,
        string operationName,
        bool shouldRetryOnConcurrencyException = true)
    {
        await ExecuteWithRetry(async () =>
        {
            await operation();
            return Task.CompletedTask;
        }, operationName, shouldRetryOnConcurrencyException);
    }

    /// <summary>
    /// Reads all lines from the CSV file including the header.
    /// </summary>
    private async Task<(string header, List<string> dataLines)> ReadAllLinesAsync(FileStream fileStream)
    {
        using var reader = new StreamReader(fileStream, leaveOpen: true);

        var header = await reader.ReadLineAsync();
        var dataLines = new List<string>();

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            dataLines.Add(line);
        }

        return (header!, dataLines);
    }

    /// <summary>
    /// Writes all lines back to the CSV file, clearing existing content first.
    /// </summary>
    private async Task WriteAllLinesAsync(FileStream fileStream, IEnumerable<string> lines)
    {
        fileStream.SetLength(0); // Clear file
        fileStream.Seek(0, SeekOrigin.Begin);

        using var writer = new StreamWriter(fileStream, leaveOpen: true);
        foreach (var line in lines)
        {
            await writer.WriteLineAsync(line);
        }
        await writer.FlushAsync();
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        try
        {
            using var fileStream = OpenFileForReading();
            using var reader = new StreamReader(fileStream);

            await reader.ReadLineAsync(); // Skip header

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var product = ParseLine(line);
                if (product?.Id == id)
                {
                    return product;
                }
            }

            _logger.LogDebug("Product with ID {ProductId} not found in CSV", id);
            return null;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "CSV file not found at {FilePath}", _csvFilePath);
            throw new FileNotFoundException(Path.GetFileName(_csvFilePath), _csvFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error reading CSV file at {FilePath}", _csvFilePath);
            throw new FileNotFoundException(Path.GetFileName(_csvFilePath), _csvFilePath);
        }
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        using var fileStream = OpenFileForReading();
        using var reader = new StreamReader(fileStream);

        var products = new List<Product>();
        await reader.ReadLineAsync(); // Skip header

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var product = ParseLine(line);
            if (product != null)
            {
                products.Add(product);
            }
        }

        return products;
    }

    public async Task<Product> CreateAsync(Product product)
    {
        return await ExecuteWithRetry(async () =>
        {
            using var fileStream = OpenFileForWriting();
            using var reader = new StreamReader(fileStream, leaveOpen: true);

            // Read all lines to find max ID
            await reader.ReadLineAsync(); // Skip header
            var maxId = 0;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var existingProduct = ParseLine(line);
                if (existingProduct != null && existingProduct.Id > maxId)
                {
                    maxId = existingProduct.Id;
                }
            }

            var nextId = maxId + 1;

            var newProduct = new Product(
                nextId,
                product.Name,
                product.Description,
                product.ImageUrl,
                product.Price,
                product.Rating,
                product.Specifications,
                version: 0
            );

            var newLine = FormatLine(newProduct);

            // Append to file
            using var writer = new StreamWriter(fileStream, leaveOpen: true);
            fileStream.Seek(0, SeekOrigin.End);
            await writer.WriteLineAsync(newLine);
            await writer.FlushAsync();

            _logger.LogDebug("Product persisted to CSV with ID {ProductId}", nextId);

            return newProduct;
        }, "create product");
    }

    public async Task UpdateAsync(Product product)
    {
        await ExecuteWithRetry(async () =>
        {
            using var fileStream = OpenFileForWriting();

            // Read all lines
            var (header, dataLines) = await ReadAllLinesAsync(fileStream);

            var found = false;
            var currentVersion = 0;

            foreach (var line in dataLines)
            {
                var existingProduct = ParseLine(line);
                if (existingProduct?.Id == product.Id)
                {
                    found = true;
                    currentVersion = existingProduct.Version;
                    break;
                }
            }

            if (!found)
            {
                throw new ProductNotFoundException(product.Id);
            }

            // Check for version conflict (optimistic concurrency check)
            if (product.Version != currentVersion)
            {
                throw new ConcurrencyException(
                    $"Product {product.Id} was modified by another process. " +
                    $"Expected version {product.Version}, but current version is {currentVersion}.");
            }

            // Increment version
            product.IncrementVersion();

            // Update the product in memory
            var updatedLines = new List<string> { header };
            foreach (var line in dataLines)
            {
                var existingProduct = ParseLine(line);
                if (existingProduct?.Id == product.Id)
                {
                    updatedLines.Add(FormatLine(product));
                }
                else
                {
                    updatedLines.Add(line);
                }
            }

            // Write back to file
            await WriteAllLinesAsync(fileStream, updatedLines);

            _logger.LogDebug("Product {ProductId} updated to version {Version}", product.Id, product.Version);
        }, "update product", shouldRetryOnConcurrencyException: false);
    }

    public async Task DeleteAsync(int id)
    {
        await ExecuteWithRetry(async () =>
        {
            using var fileStream = OpenFileForWriting();

            // Read all lines
            var (header, dataLines) = await ReadAllLinesAsync(fileStream);

            var initialCount = dataLines.Count;
            var linesToKeep = new List<string> { header };

            foreach (var line in dataLines)
            {
                var existingProduct = ParseLine(line);
                if (existingProduct?.Id != id)
                {
                    linesToKeep.Add(line);
                }
            }

            // Write back to file
            await WriteAllLinesAsync(fileStream, linesToKeep);

            if (linesToKeep.Count - 1 < initialCount)
            {
                _logger.LogDebug("Deleted product {ProductId} from CSV", id);
            }
        }, "delete product");
    }

    private Product? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var values = SplitCsvLine(line);
        if (values.Length != 10)
        {
            _logger.LogWarning("CSV line has invalid format: expected 10 fields, got {FieldCount}", values.Length);
            _logger.LogWarning("Line content: {Line}", line);
            _logger.LogWarning("Parsed fields: [{Fields}]", string.Join("] [", values));
            return null;
        }

        try
        {
            return new Product(
                id: int.Parse(values[0]),
                name: values[1],
                description: values[2],
                imageUrl: values[3],
                price: new Domain.ValueObjects.Price(decimal.Parse(values[4], CultureInfo.InvariantCulture)),
                rating: new Domain.ValueObjects.Rating(decimal.Parse(values[5], CultureInfo.InvariantCulture), 1),
                specifications: new Domain.ValueObjects.ProductSpecifications(
                    brand: values[6],
                    color: values[7],
                    weight: values[8]
                ),
                version: int.Parse(values[9])
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse CSV line: {Line}", line);
            return null;
        }
    }

    private static string FormatLine(Product product)
    {
        return string.Join(",",
            product.Id,
            EscapeCsvField(product.Name),
            EscapeCsvField(product.Description),
            EscapeCsvField(product.ImageUrl),
            product.Price.Value.ToString(CultureInfo.InvariantCulture),
            product.Rating.Value.ToString(CultureInfo.InvariantCulture),
            EscapeCsvField(product.Specifications.Brand),
            EscapeCsvField(product.Specifications.Color),
            EscapeCsvField(product.Specifications.Weight),
            product.Version
        );
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var currentField = new StringBuilder();
        var insideQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            var currentChar = line[i];

            if (currentChar == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (currentChar == ',' && !insideQuotes)
            {
                result.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(currentChar);
            }
        }

        result.Add(currentField.ToString());
        return result.ToArray();
    }
}
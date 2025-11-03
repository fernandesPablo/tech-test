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

        _logger.LogWarning("=== PRODUCT REPOSITORY INIT ===");
        _logger.LogWarning("CsvFilePath from config: '{Path}'", configuration.CsvFilePath ?? "NULL");
        _logger.LogWarning("BaseDirectory: '{Dir}'", configuration.BaseDirectory);

        // Usa CsvPathResolver para centralizar a lógica de resolução de caminho
        _csvFilePath = CsvPathResolver.ResolvePath(configuration);
        _logger.LogWarning("Resolved CSV path: {Path}", _csvFilePath);

        _logger.LogWarning("=== FINAL REPOSITORY CSV PATH: {Path} ===", _csvFilePath);
        EnsureFileExists();
    }

    private void EnsureFileExists()
    {
        var directory = Path.GetDirectoryName(_csvFilePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory!);
            _logger.LogWarning("Created CSV directory: {Directory}", directory);
        }

        if (!File.Exists(_csvFilePath))
        {
            _logger.LogWarning("CSV file does NOT exist, creating with header only: {Path}", _csvFilePath);
            File.WriteAllText(_csvFilePath, _header + Environment.NewLine);
            _logger.LogWarning("Created CSV file with header: {FilePath}", _csvFilePath);
        }
        else
        {
            _logger.LogWarning("CSV file EXISTS: {Path}", _csvFilePath);
            var lineCount = File.ReadAllLines(_csvFilePath).Length;
            _logger.LogWarning("CSV has {LineCount} lines", lineCount);
        }
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        try
        {
            EnsureFileExists(); // Ensure file exists before every operation

            // Read with shared lock (multiple readers and writers allowed for concurrent access)
            using var fileStream = new FileStream(
                _csvFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

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
            throw new DataFileNotFoundException(Path.GetFileName(_csvFilePath), _csvFilePath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error reading CSV file at {FilePath}", _csvFilePath);
            throw new DataFileNotFoundException(Path.GetFileName(_csvFilePath), _csvFilePath);
        }
    }

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        EnsureFileExists(); // Ensure file exists before every operation

        // Read with shared lock (multiple readers and writers allowed for concurrent access)
        using var fileStream = new FileStream(
            _csvFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

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
        const int maxRetries = 5;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                EnsureFileExists(); // Ensure file exists before every operation

                // Exclusive lock: no other process can read or write
                using var fileStream = new FileStream(
                    _csvFilePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

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
            }
            catch (IOException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                _logger.LogDebug("File locked during create, retrying... ({Attempt}/{Max})", retryCount, maxRetries);
                await Task.Delay(50 * retryCount); // Exponential backoff
            }
        }

        throw new IOException($"Failed to create product after {maxRetries} retries due to file lock contention");
    }

    public async Task UpdateAsync(Product product)
    {
        const int maxRetries = 5;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                EnsureFileExists(); // Ensure file exists before every operation

                // Exclusive lock: no other process can read or write
                using var fileStream = new FileStream(
                    _csvFilePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

                using var reader = new StreamReader(fileStream, leaveOpen: true);

                // Read all lines
                var lines = new List<string>();
                var header = await reader.ReadLineAsync();
                lines.Add(header!);

                var found = false;
                var currentVersion = 0;

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var existingProduct = ParseLine(line);
                    if (existingProduct?.Id == product.Id)
                    {
                        found = true;
                        currentVersion = existingProduct.Version;
                    }
                    lines.Add(line);
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
                var updatedLines = new List<string> { header! };
                foreach (var l in lines.Skip(1))
                {
                    var existingProduct = ParseLine(l);
                    if (existingProduct?.Id == product.Id)
                    {
                        updatedLines.Add(FormatLine(product));
                    }
                    else
                    {
                        updatedLines.Add(l);
                    }
                }

                // Write back to file
                fileStream.SetLength(0); // Clear file
                fileStream.Seek(0, SeekOrigin.Begin);

                using var writer = new StreamWriter(fileStream, leaveOpen: true);
                foreach (var l in updatedLines)
                {
                    await writer.WriteLineAsync(l);
                }
                await writer.FlushAsync();

                _logger.LogDebug("Product {ProductId} updated to version {Version}", product.Id, product.Version);
                return;
            }
            catch (ConcurrencyException)
            {
                // Don't retry on concurrency conflicts, let it bubble up
                throw;
            }
            catch (IOException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                _logger.LogDebug("File locked during update, retrying... ({Attempt}/{Max})", retryCount, maxRetries);
                await Task.Delay(50 * retryCount); // Exponential backoff
            }
        }

        throw new IOException($"Failed to update product after {maxRetries} retries due to file lock contention");
    }

    public async Task DeleteAsync(int id)
    {
        const int maxRetries = 5;
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                EnsureFileExists(); // Ensure file exists before every operation

                // Exclusive lock: no other process can read or write
                using var fileStream = new FileStream(
                    _csvFilePath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);

                using var reader = new StreamReader(fileStream, leaveOpen: true);

                // Read all lines
                var lines = new List<string>();
                var header = await reader.ReadLineAsync();
                lines.Add(header!);

                var initialCount = 0;
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    var existingProduct = ParseLine(line);
                    if (existingProduct?.Id != id)
                    {
                        lines.Add(line);
                    }
                    initialCount++;
                }

                // Write back to file
                fileStream.SetLength(0); // Clear file
                fileStream.Seek(0, SeekOrigin.Begin);

                using var writer = new StreamWriter(fileStream, leaveOpen: true);
                foreach (var l in lines)
                {
                    await writer.WriteLineAsync(l);
                }
                await writer.FlushAsync();

                if (lines.Count - 1 < initialCount)
                {
                    _logger.LogDebug("Deleted product {ProductId} from CSV", id);
                }

                return;
            }
            catch (IOException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                _logger.LogDebug("File locked during delete, retrying... ({Attempt}/{Max})", retryCount, maxRetries);
                await Task.Delay(50 * retryCount); // Exponential backoff
            }
        }

        throw new IOException($"Failed to delete product after {maxRetries} retries due to file lock contention");
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
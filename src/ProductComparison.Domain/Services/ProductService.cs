using Microsoft.Extensions.Logging;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Domain.Models;
using ProductComparison.Domain.ValueObjects;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProductComparison.Domain.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _repository;
    private readonly IProductAuditLogRepository _auditRepository;
    private readonly ILogger<ProductService> _logger;
    private readonly ICacheService _cache;

    public ProductService(
        IProductRepository repository,
        IProductAuditLogRepository auditRepository,
        ILogger<ProductService> logger,
        ICacheService cache)
    {
        _repository = repository;
        _auditRepository = auditRepository;
        _logger = logger;
        _cache = cache;
    }

    public async Task<PagedResult<ProductResponseDto>> GetAllAsync(int page = 1, int pageSize = 10)
    {
        // Validate pagination parameters
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100; // Max page size limit

        var cacheKey = $"products:list:page:{page}:size:{pageSize}";

        return await ExecuteWithOperationScopeAsync("GetAllProducts", async () =>
        {
            _logger.LogInformation("Retrieving products page {Page} with size {PageSize}", page, pageSize);

            var result = await ExecuteWithCacheAsync(
                cacheKey,
                TimeSpan.FromMinutes(15),
                async () =>
                {
                    var allProducts = await _repository.GetAllAsync();
                    var productList = allProducts.ToList();
                    var totalCount = productList.Count;

                    var pagedProducts = productList
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .Select(ToResponse)
                        .ToList();

                    var pagedResult = new PagedResult<ProductResponseDto>(
                        pagedProducts,
                        totalCount,
                        page,
                        pageSize
                    );

                    _logger.LogInformation(
                        "Successfully retrieved page {Page} with {ItemCount} of {TotalCount} products",
                        page, pagedProducts.Count, totalCount);

                    return pagedResult;
                },
                $"Products page {page} retrieved from cache");

            return result;
        }, new Dictionary<string, object>
        {
            ["Page"] = page,
            ["PageSize"] = pageSize,
            ["CacheKey"] = cacheKey
        });
    }

    public async Task<ProductResponseDto> GetByIdAsync(Guid id)
    {
        var cacheKey = $"products:details:{id}";

        return await ExecuteWithOperationScopeAsync("GetProductById", async () =>
        {
            _logger.LogInformation("Fetching product with ID: {ProductId}", id);

            var result = await ExecuteWithCacheAsync(
                cacheKey,
                TimeSpan.FromMinutes(30),
                async () =>
                {
                    var product = await _repository.GetByIdAsync(id);

                    if (product == null)
                    {
                        _logger.LogWarning("Product with ID {ProductId} not found", id);
                        throw new ProductNotFoundException(id);
                    }

                    var response = ToResponse(product);
                    _logger.LogInformation("Successfully retrieved product {ProductId}: {ProductName}", id, product.Name);
                    return response;
                },
                $"Product {id} retrieved from cache");

            return result;
        }, new Dictionary<string, object>
        {
            ["ProductId"] = id,
            ["CacheKey"] = cacheKey
        });
    }

    public async Task<ProductComparisonDto> CompareAsync(string productIds)
    {
        // Generate a stable, collision-resistant cache key using SHA256 hash
        var cacheKey = GenerateCacheKeyForComparison(productIds);

        return await ExecuteWithOperationScopeAsync("CompareProducts", async () =>
        {
            if (string.IsNullOrWhiteSpace(productIds))
            {
                _logger.LogWarning("Product comparison attempted with empty IDs");
                throw new ProductValidationException("No product IDs provided");
            }

            var result = await ExecuteWithCacheAsync(
                cacheKey,
                TimeSpan.FromMinutes(20),
                async () =>
                {
                    // Validate and parse IDs upfront to catch errors immediately
                    var idStrings = productIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(id => id.Trim())
                        .ToList();

                    if (!idStrings.Any())
                    {
                        _logger.LogWarning("Product comparison attempted with no valid IDs after parsing");
                        throw new ProductValidationException("No product IDs provided");
                    }

                    var ids = new List<Guid>();
                    foreach (var idString in idStrings)
                    {
                        if (!Guid.TryParse(idString, out var parsedId))
                        {
                            _logger.LogWarning("Invalid product ID format during comparison: {InvalidId}", idString);
                            throw new ProductValidationException($"Invalid product ID format: {idString}");
                        }
                        ids.Add(parsedId);
                    }

                    _logger.LogInformation("Comparing {ProductCount} products with IDs: {ProductIds}", ids.Count, string.Join(", ", ids));

                    var products = new List<Product>();
                    foreach (var id in ids)
                    {
                        var product = await _repository.GetByIdAsync(id);
                        if (product == null)
                        {
                            _logger.LogWarning("Product with ID {ProductId} not found during comparison", id);
                            throw new ProductNotFoundException(id);
                        }
                        products.Add(product);
                    }

                    var maxPrice = products.Max(p => p.Price.Value);
                    var minPrice = products.Min(p => p.Price.Value);
                    var avgPrice = products.Average(p => p.Price.Value);

                    _logger.LogInformation(
                        "Comparison completed: MaxPrice={MaxPrice}, MinPrice={MinPrice}, AvgPrice={AvgPrice}, PriceDifference={PriceDifference}",
                        maxPrice, minPrice, avgPrice, maxPrice - minPrice);

                    return new ProductComparisonDto
                    {
                        Products = products.Select(ToResponse).ToList(),
                        Differences = new List<string>
                        {
                            $"Price difference: {maxPrice - minPrice:C2}",
                            $"Average price: {avgPrice:C2}"
                        }
                    };
                },
                $"Product comparison for IDs {productIds} retrieved from cache");

            return result;
        }, new Dictionary<string, object>
        {
            ["CacheKey"] = cacheKey
        });
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto createDto)
    {
        return await ExecuteWithOperationScopeAsync("CreateProduct", async () =>
        {
            _logger.LogInformation("Creating new product: {ProductName} with price {Price}, ID: {ProductId}", createDto.Name, createDto.Price, createDto.Id);

            // Check if product with this ID already exists (idempotency)
            var existing = await _repository.GetByIdAsync(createDto.Id);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Product with ID {ProductId} already exists. Returning existing product for idempotency. Name: {ProductName}",
                    createDto.Id, existing.Name);
                return ToResponse(existing);
            }

            var product = ToDomain(createDto);
            var created = await _repository.CreateAsync(product);

            // Create audit log for product creation
            var auditLog = new ProductAuditLog(
                created.Id,
                AuditOperationType.Create,
                created.Version,
                JsonSerializer.Serialize(ToResponse(created)),
                $"Product created: {created.Name}");
            await _auditRepository.CreateAsync(auditLog);

            _logger.LogInformation(
                "Product created successfully with ID {ProductId}: {ProductName}, Price: {Price}",
                created.Id, created.Name, created.Price.Value);

            await InvalidateListCacheAsync();

            return ToResponse(created);
        });
    }

    /// <summary>
    /// Executes an operation with caching support. Checks cache first, and if not found,
    /// executes the fetch operation and caches the result.
    /// Properly handles null validation: null results are not cached.
    /// </summary>
    private async Task<T> ExecuteWithCacheAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<Task<T>> fetchOperation,
        string? cacheHitMessage = null) where T : class
    {
        // Try to get from cache
        var cachedResult = await _cache.GetAsync<T>(cacheKey);
        if (cachedResult != null)
        {
            if (!string.IsNullOrEmpty(cacheHitMessage))
            {
                _logger.LogInformation("{Message} (Cache Hit)", cacheHitMessage);
            }
            return cachedResult;
        }

        // Fetch from source
        var result = await fetchOperation();

        // Only cache non-null results to avoid caching unintended nulls
        if (result != null)
        {
            await _cache.SetAsync(cacheKey, result, ttl);
        }
        else
        {
            _logger.LogDebug("Skipping cache for key {CacheKey} - result is null", cacheKey);
        }

        return result!; // Guaranteed non-null by fetchOperation contract or exception
    }

    /// <summary>
    /// Executes an operation within a logging scope with structured metadata.
    /// </summary>
    private async Task<T> ExecuteWithOperationScopeAsync<T>(
        string operation,
        Func<Task<T>> action,
        Dictionary<string, object>? additionalScope = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["Operation"] = operation
        };

        if (additionalScope != null)
        {
            foreach (var kvp in additionalScope)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }

        using (_logger.BeginScope(scope))
        {
            return await action();
        }
    }

    /// <summary>
    /// Executes a void operation within a logging scope with structured metadata.
    /// </summary>
    private async Task ExecuteWithOperationScopeAsync(
        string operation,
        Func<Task> action,
        Dictionary<string, object>? additionalScope = null)
    {
        var scope = new Dictionary<string, object>
        {
            ["Operation"] = operation
        };

        if (additionalScope != null)
        {
            foreach (var kvp in additionalScope)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }

        using (_logger.BeginScope(scope))
        {
            await action();
        }
    }

    /// <summary>
    /// Invalidates all product list caches.
    /// </summary>
    private async Task InvalidateListCacheAsync()
    {
        await _cache.RemoveByPatternAsync("products:list:*");
        _logger.LogDebug("Invalidated all list cache entries");
    }

    /// <summary>
    /// Invalidates cache for a specific product and all list caches.
    /// </summary>
    private async Task InvalidateProductCacheAsync(Guid productId)
    {
        var cacheKey = $"products:details:{productId}";
        await _cache.RemoveAsync(cacheKey);
        _logger.LogDebug("Invalidated cache for product {ProductId}", productId);
        await InvalidateListCacheAsync();
    }

    /// <summary>
    /// Generates a stable, collision-resistant cache key for product comparison.
    /// Uses SHA256 hash to ensure UUIDs with special characters don't create cache key collisions.
    /// </summary>
    private string GenerateCacheKeyForComparison(string productIds)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(productIds));
        var hashHex = Convert.ToHexString(hash).ToLower()[..16]; // Use first 16 chars for reasonable length
        return $"products:comparison:{hashHex}";
    }

    public async Task<ProductResponseDto> UpdateAsync(Guid id, UpdateProductDto updateDto)
    {
        return await ExecuteWithOperationScopeAsync("UpdateProduct", async () =>
        {
            _logger.LogInformation("Updating product with ID {ProductId} (version {Version})", id, updateDto.Version);

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("Cannot update - Product with ID {ProductId} not found", id);
                throw new ProductNotFoundException(id);
            }

            // Optimistic concurrency check - client provides the version they read
            if (existing.Version != updateDto.Version)
            {
                _logger.LogWarning(
                    "Concurrency conflict updating product {ProductId}: expected version {ExpectedVersion} but current is {CurrentVersion}",
                    id, updateDto.Version, existing.Version);
                throw new ConcurrencyException(
                    $"Product was modified by another request. Expected version {updateDto.Version}, but current version is {existing.Version}.");
            }

            _logger.LogInformation(
                "Product found. Updating: Name from '{OldName}' to '{NewName}', Price from {OldPrice} to {NewPrice}",
                existing.Name, updateDto.Name, existing.Price.Value, updateDto.Price);

            var product = new Product(
                id: id,
                name: updateDto.Name,
                description: updateDto.Description,
                imageUrl: updateDto.ImageUrl,
                price: new Price(updateDto.Price),
                rating: new Rating(updateDto.Rating, existing.Rating.NumberOfRatings),
                specifications: new ProductSpecifications(
                    updateDto.Specifications.Brand,
                    updateDto.Specifications.Color,
                    updateDto.Specifications.Weight
                ),
                version: existing.Version  // Repository will increment this
            );

            await _repository.UpdateAsync(product);

            // Create audit log for product update with change summary
            var changeSummary = BuildUpdateChangeSummary(existing, product);
            var auditLog = new ProductAuditLog(
                id,
                AuditOperationType.Update,
                product.Version,
                JsonSerializer.Serialize(ToResponse(product)),
                changeSummary,
                existing.Version,
                JsonSerializer.Serialize(ToResponse(existing)));
            await _auditRepository.CreateAsync(auditLog);

            _logger.LogInformation("Product {ProductId} updated successfully to version {NewVersion}", id, product.Version);

            await InvalidateProductCacheAsync(id);

            return ToResponse(product);
        }, new Dictionary<string, object>
        {
            ["ProductId"] = id,
            ["RequestVersion"] = updateDto.Version
        });
    }

    public async Task DeleteAsync(Guid id)
    {
        await ExecuteWithOperationScopeAsync("DeleteProduct", async () =>
        {
            _logger.LogInformation("Attempting to delete product with ID {ProductId}", id);

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("Delete requested for non-existent product {ProductId} - completing successfully for idempotence", id);
                // Don't throw - make DELETE truly idempotent (RFC 9110)
                // Return 204 No Content whether product existed or not
                return;
            }

            _logger.LogInformation("Product found: {ProductName}. Proceeding with deletion", existing.Name);

            await _repository.DeleteAsync(id);

            // Create audit log for product deletion
            var auditLog = new ProductAuditLog(
                id,
                AuditOperationType.Delete,
                existing.Version + 1,
                string.Empty, // No new state for deletions
                $"Product deleted: {existing.Name} (was version {existing.Version})",
                existing.Version,
                JsonSerializer.Serialize(ToResponse(existing)));
            await _auditRepository.CreateAsync(auditLog);

            await InvalidateProductCacheAsync(id);

            _logger.LogInformation("Product {ProductId} ({ProductName}) deleted successfully", id, existing.Name);
        }, new Dictionary<string, object>
        {
            ["ProductId"] = id
        });
    }

    private static ProductResponseDto ToResponse(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Description = product.Description,
        ImageUrl = product.ImageUrl,
        Price = product.Price.Value,
        Rating = product.Rating.Value,
        Specifications = new ProductSpecificationsDto
        {
            Brand = product.Specifications.Brand,
            Color = product.Specifications.Color,
            Weight = product.Specifications.Weight
        },
        Version = product.Version
    };

    private static Product ToDomain(CreateProductDto request) => new(
        id: request.Id, // Client-provided GUID for idempotent POST
        name: request.Name,
        description: request.Description,
        imageUrl: request.ImageUrl,
        price: new Price(request.Price),
        rating: new Rating(request.Rating, 1), // Assume 1 avaliação inicial
        specifications: new ProductSpecifications(
            request.Specifications.Brand,
            request.Specifications.Color,
            request.Specifications.Weight
        )
    );

    /// <summary>
    /// Builds a human-readable summary of what changed during an update operation.
    /// Useful for audit logs and debugging.
    /// </summary>
    private static string BuildUpdateChangeSummary(Product oldProduct, Product newProduct)
    {
        var changes = new List<string>();

        if (oldProduct.Name != newProduct.Name)
            changes.Add($"Name: '{oldProduct.Name}' → '{newProduct.Name}'");

        if (oldProduct.Description != newProduct.Description)
            changes.Add($"Description: '{oldProduct.Description}' → '{newProduct.Description}'");

        if (oldProduct.ImageUrl != newProduct.ImageUrl)
            changes.Add($"ImageUrl: '{oldProduct.ImageUrl}' → '{newProduct.ImageUrl}'");

        if (oldProduct.Price.Value != newProduct.Price.Value)
            changes.Add($"Price: {oldProduct.Price.Value:C} → {newProduct.Price.Value:C}");

        if (oldProduct.Rating.Value != newProduct.Rating.Value)
            changes.Add($"Rating: {oldProduct.Rating.Value} → {newProduct.Rating.Value}");

        if (oldProduct.Specifications.Brand != newProduct.Specifications.Brand)
            changes.Add($"Brand: '{oldProduct.Specifications.Brand}' → '{newProduct.Specifications.Brand}'");

        if (oldProduct.Specifications.Color != newProduct.Specifications.Color)
            changes.Add($"Color: '{oldProduct.Specifications.Color}' → '{newProduct.Specifications.Color}'");

        if (oldProduct.Specifications.Weight != newProduct.Specifications.Weight)
            changes.Add($"Weight: '{oldProduct.Specifications.Weight}' → '{newProduct.Specifications.Weight}'");

        return changes.Count > 0 
            ? $"Updated: {string.Join(", ", changes)}"
            : "No changes detected";
    }
}
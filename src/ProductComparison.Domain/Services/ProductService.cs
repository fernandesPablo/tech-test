using Microsoft.Extensions.Logging;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.Extensions;
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
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

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
                        .Select(p => p.ToDto())
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

                    var response = product.ToDto();
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
                    var ids = ParseProductIds(productIds);
                    _logger.LogInformation("Comparing {ProductCount} products with IDs: {ProductIds}", ids.Count, string.Join(", ", ids));

                    var products = await FetchProducts(ids);

                    var maxPrice = products.Max(p => p.Price.Value);
                    var minPrice = products.Min(p => p.Price.Value);
                    var avgPrice = products.Average(p => p.Price.Value);

                    _logger.LogInformation(
                        "Comparison completed: MaxPrice={MaxPrice}, MinPrice={MinPrice}, AvgPrice={AvgPrice}, PriceDifference={PriceDifference}",
                        maxPrice, minPrice, avgPrice, maxPrice - minPrice);

                    return new ProductComparisonDto
                    {
                        Products = products.Select(p => p.ToDto()).ToList(),
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

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto createDto, string idempotencyKey)
    {
        return await ExecuteWithOperationScopeAsync("CreateProduct", async () =>
        {
            var cacheKey = $"idempotency:{idempotencyKey}";

            _logger.LogInformation("Creating new product: {ProductName} with price {Price}, IdempotencyKey: {IdempotencyKey}",
                createDto.Name, createDto.Price, idempotencyKey);

            // Check if this idempotency key was already processed
            var cachedResponse = await _cache.GetAsync<ProductResponseDto>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogInformation(
                    "Idempotency key {IdempotencyKey} already processed. Returning cached response for product {ProductId}",
                    idempotencyKey, cachedResponse.Id);
                return cachedResponse;
            }

            var product = createDto.ToEntity();
            var created = await _repository.CreateAsync(product);

            await LogAuditAsync(created.Id, AuditOperationType.Create, newState: created);

            var response = created.ToDto();

            // Cache the response with 24-hour TTL for idempotency
            await _cache.SetAsync(cacheKey, response, TimeSpan.FromHours(24));

            _logger.LogInformation(
                "Product created successfully with ID {ProductId}: {ProductName}, Price: {Price}",
                created.Id, created.Name, created.Price.Value);

            await InvalidateListCacheAsync();

            return response;
        });
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

            // Optimistic concurrency check
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
                version: existing.Version
            );

            await _repository.UpdateAsync(product);

            await LogAuditAsync(id, AuditOperationType.Update, newState: product, oldState: existing);

            _logger.LogInformation("Product {ProductId} updated successfully to version {NewVersion}", id, product.Version);

            await InvalidateProductCacheAsync(id);

            return product.ToDto();
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
                return; // Idempotent - don't throw
            }

            _logger.LogInformation("Product found: {ProductName}. Proceeding with deletion", existing.Name);

            await _repository.DeleteAsync(id);

            await LogAuditAsync(id, AuditOperationType.Delete, oldState: existing);

            await InvalidateProductCacheAsync(id);

            _logger.LogInformation("Product {ProductId} ({ProductName}) deleted successfully", id, existing.Name);
        }, new Dictionary<string, object>
        {
            ["ProductId"] = id
        });
    }

    #region Helper Methods

    /// <summary>
    /// REFACTORED: Centralized audit logging to eliminate duplication
    /// </summary>
    private async Task LogAuditAsync(
        Guid productId,
        AuditOperationType operation,
        Product? newState = null,
        Product? oldState = null,
        string? customMessage = null)
    {
        var message = customMessage ?? operation switch
        {
            AuditOperationType.Create => $"Product created: {newState?.Name}",
            AuditOperationType.Update => BuildUpdateChangeSummary(oldState!, newState!),
            AuditOperationType.Delete => $"Product deleted: {oldState?.Name} (was version {oldState?.Version})",
            _ => $"Operation {operation} performed"
        };

        var auditLog = new ProductAuditLog(
            productId,
            operation,
            newState?.Version ?? (oldState?.Version + 1) ?? 0,
            newState != null ? JsonSerializer.Serialize(newState.ToDto()) : string.Empty,
            message,
            oldState?.Version,
            oldState != null ? JsonSerializer.Serialize(oldState.ToDto()) : null
        );

        await _auditRepository.CreateAsync(auditLog);
        _logger.LogDebug("Audit log created for product {ProductId}: {Operation}", productId, operation);
    }

    /// <summary>
    /// REFACTORED: Extract ID parsing logic
    /// </summary>
    private List<Guid> ParseProductIds(string productIds)
    {
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

        return ids;
    }

    /// <summary>
    /// REFACTORED: Extract product fetching logic
    /// </summary>
    private async Task<List<Product>> FetchProducts(List<Guid> ids)
    {
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
        return products;
    }

    private async Task<T> ExecuteWithCacheAsync<T>(
        string cacheKey,
        TimeSpan ttl,
        Func<Task<T>> fetchOperation,
        string? cacheHitMessage = null) where T : class
    {
        var cachedResult = await _cache.GetAsync<T>(cacheKey);
        if (cachedResult != null)
        {
            if (!string.IsNullOrEmpty(cacheHitMessage))
            {
                _logger.LogInformation("{Message} (Cache Hit)", cacheHitMessage);
            }
            return cachedResult;
        }

        var result = await fetchOperation();

        if (result != null)
        {
            await _cache.SetAsync(cacheKey, result, ttl);
        }
        else
        {
            _logger.LogDebug("Skipping cache for key {CacheKey} - result is null", cacheKey);
        }

        return result!;
    }

    /// <summary>
    /// REFACTORED: Simplified overload reuses generic version
    /// </summary>
    private async Task<T> ExecuteWithOperationScopeAsync<T>(
        string operation,
        Func<Task<T>> action,
        Dictionary<string, object>? additionalScope = null)
    {
        var scope = BuildOperationScope(operation, additionalScope);

        using (_logger.BeginScope(scope))
        {
            return await action();
        }
    }

    private async Task ExecuteWithOperationScopeAsync(
        string operation,
        Func<Task> action,
        Dictionary<string, object>? additionalScope = null)
    {
        await ExecuteWithOperationScopeAsync(operation, async () =>
        {
            await action();
            return Task.CompletedTask;
        }, additionalScope);
    }

    /// <summary>
    /// REFACTORED: Extract scope building to eliminate duplication
    /// </summary>
    private static Dictionary<string, object> BuildOperationScope(
        string operation,
        Dictionary<string, object>? additionalScope)
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

        return scope;
    }

    private async Task InvalidateListCacheAsync()
    {
        await _cache.RemoveByPatternAsync("products:list:*");
        _logger.LogDebug("Invalidated all list cache entries");
    }

    private async Task InvalidateProductCacheAsync(Guid productId)
    {
        var cacheKey = $"products:details:{productId}";
        await _cache.RemoveAsync(cacheKey);
        _logger.LogDebug("Invalidated cache for product {ProductId}", productId);
        await InvalidateListCacheAsync();
    }

    private string GenerateCacheKeyForComparison(string productIds)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(productIds));
        var hashHex = Convert.ToHexString(hash).ToLower()[..16];
        return $"products:comparison:{hashHex}";
    }

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

    #endregion
}

// REFACTORED: Extension methods
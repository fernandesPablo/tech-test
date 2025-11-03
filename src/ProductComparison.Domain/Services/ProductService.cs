using Microsoft.Extensions.Logging;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Domain.Models;
using ProductComparison.Domain.ValueObjects;

namespace ProductComparison.Domain.Services;

public class ProductService : IProductService
{
    private readonly IProductRepository _repository;
    private readonly ILogger<ProductService> _logger;
    private readonly ICacheService _cache;

    public ProductService(
        IProductRepository repository,
        ILogger<ProductService> logger,
        ICacheService cache)
    {
        _repository = repository;
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

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "GetAllProducts",
            ["Page"] = page,
            ["PageSize"] = pageSize,
            ["CacheKey"] = cacheKey
        }))
        {
            _logger.LogInformation("Retrieving products page {Page} with size {PageSize}", page, pageSize);

            // Try to get from cache
            var cachedResult = await _cache.GetAsync<PagedResult<ProductResponseDto>>(cacheKey);
            if (cachedResult != null)
            {
                _logger.LogInformation("Products page {Page} retrieved from cache (Cache Hit)", page);
                return cachedResult;
            }

            // Get all products from repository
            var allProducts = await _repository.GetAllAsync();
            var productList = allProducts.ToList();
            var totalCount = productList.Count;

            // Apply pagination
            var pagedProducts = productList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(ToResponse)
                .ToList();

            var result = new PagedResult<ProductResponseDto>(
                pagedProducts,
                totalCount,
                page,
                pageSize
            );

            _logger.LogInformation(
                "Successfully retrieved page {Page} with {ItemCount} of {TotalCount} products",
                page, pagedProducts.Count, totalCount);

            // Cache the result with TTL
            var ttl = TimeSpan.FromMinutes(15);
            await _cache.SetAsync(cacheKey, result, ttl);

            return result;
        }
    }

    public async Task<ProductResponseDto> GetByIdAsync(int id)
    {
        var cacheKey = $"products:details:{id}";

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "GetProductById",
            ["ProductId"] = id,
            ["CacheKey"] = cacheKey
        }))
        {
            _logger.LogInformation("Fetching product with ID: {ProductId}", id);

            // Try to get from cache
            var cachedProduct = await _cache.GetAsync<ProductResponseDto>(cacheKey);
            if (cachedProduct != null)
            {
                _logger.LogInformation("Product {ProductId} retrieved from cache (Cache Hit)", id);
                return cachedProduct;
            }

            var product = await _repository.GetByIdAsync(id);

            if (product == null)
            {
                _logger.LogWarning("Product with ID {ProductId} not found", id);
                throw new ProductNotFoundException(id);
            }

            var response = ToResponse(product);

            _logger.LogInformation("Successfully retrieved product {ProductId}: {ProductName}", id, product.Name);

            // Cache the result with TTL
            var ttl = TimeSpan.FromMinutes(30);
            await _cache.SetAsync(cacheKey, response, ttl);

            return response;
        }
    }

    public async Task<ProductComparisonDto> CompareAsync(string productIds)
    {
        var cacheKey = $"products:comparison:{productIds.Replace(",", ":")}";

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "CompareProducts",
            ["CacheKey"] = cacheKey
        }))
        {
            if (string.IsNullOrWhiteSpace(productIds))
            {
                _logger.LogWarning("Product comparison attempted with empty IDs");
                throw new ProductValidationException("No product IDs provided");
            }

            // Try to get from cache
            var cachedComparison = await _cache.GetAsync<ProductComparisonDto>(cacheKey);
            if (cachedComparison != null)
            {
                _logger.LogInformation("Product comparison for IDs {ProductIds} retrieved from cache (Cache Hit)", productIds);
                return cachedComparison;
            }

            var ids = productIds.Split(',')
                .Select(id => int.TryParse(id, out var parsed) ? parsed : throw new ProductValidationException($"Invalid product ID: {id}"))
                .ToList();

            if (!ids.Any())
            {
                _logger.LogWarning("Product comparison attempted with no valid IDs after parsing");
                throw new ProductValidationException("No product IDs provided");
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

            var result = new ProductComparisonDto
            {
                Products = products.Select(ToResponse).ToList(),
                Differences = new List<string>
                {
                    $"Price difference: {maxPrice - minPrice:C2}",
                    $"Average price: {avgPrice:C2}"
                }
            };

            // Cache the result with TTL
            var ttl = TimeSpan.FromMinutes(20);
            await _cache.SetAsync(cacheKey, result, ttl);

            return result;
        }
    }

    public async Task<ProductResponseDto> CreateAsync(CreateProductDto createDto)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "CreateProduct"
        }))
        {
            _logger.LogInformation("Creating new product: {ProductName} with price {Price}", createDto.Name, createDto.Price);

            var product = ToDomain(createDto);
            var created = await _repository.CreateAsync(product);

            _logger.LogInformation(
                "Product created successfully with ID {ProductId}: {ProductName}, Price: {Price}",
                created.Id, created.Name, created.Price.Value);

            await InvalidateListCacheAsync();

            return ToResponse(created);
        }
    }

    private async Task InvalidateListCacheAsync()
    {
        await _cache.RemoveByPatternAsync("products:list:*");
        _logger.LogDebug("Invalidated all list cache entries");
    }

    public async Task<ProductResponseDto> UpdateAsync(int id, UpdateProductDto updateDto)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "UpdateProduct",
            ["ProductId"] = id
        }))
        {
            _logger.LogInformation("Updating product with ID {ProductId}", id);

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("Cannot update - Product with ID {ProductId} not found", id);
                throw new ProductNotFoundException(id);
            }

            _logger.LogInformation(
                "Product found. Updating: Name from '{OldName}' to '{NewName}', Price from {OldPrice} to {NewPrice}",
                existing.Name, updateDto.Name, existing.Price.Value, updateDto.Price);

            var product = new Product(
                id: id,
                name: updateDto.Name ?? existing.Name,
                description: updateDto.Description ?? existing.Description,
                imageUrl: updateDto.ImageUrl ?? existing.ImageUrl,
                price: new Price(updateDto.Price),
                rating: existing.Rating,
                specifications: new ProductSpecifications(
                    updateDto.Specifications.Brand ?? existing.Specifications.Brand,
                    updateDto.Specifications.Color ?? existing.Specifications.Color,
                    updateDto.Specifications.Weight ?? existing.Specifications.Weight
                ),
                version: existing.Version
            );

            await _repository.UpdateAsync(product);

            _logger.LogInformation("Product {ProductId} updated successfully", id);

            // Invalidate specific product cache and all list cache
            var cacheKey = $"products:details:{id}";
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Invalidated cache for product {ProductId}", id);

            await InvalidateListCacheAsync();

            return ToResponse(product);
        }
    }

    public async Task DeleteAsync(int id)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["Operation"] = "DeleteProduct",
            ["ProductId"] = id
        }))
        {
            _logger.LogInformation("Attempting to delete product with ID {ProductId}", id);

            var existing = await _repository.GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("Cannot delete - Product with ID {ProductId} not found", id);
                throw new ProductNotFoundException(id);
            }

            _logger.LogInformation("Product found: {ProductName}. Proceeding with deletion", existing.Name);

            await _repository.DeleteAsync(id);

            // Invalidate specific product cache and all list cache
            var cacheKey = $"products:details:{id}";
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Invalidated cache for deleted product {ProductId}", id);

            await InvalidateListCacheAsync();

            _logger.LogInformation("Product {ProductId} ({ProductName}) deleted successfully", id, existing.Name);
        }
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
        }
    };

    private static Product ToDomain(CreateProductDto request) => new(
        id: 0, // ID será definido pelo repositório
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
}
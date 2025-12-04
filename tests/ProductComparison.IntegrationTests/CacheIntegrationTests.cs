using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Models;
using ProductComparison.IntegrationTests.DTOs;
using ProductComparison.IntegrationTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace ProductComparison.IntegrationTests;

/// <summary>
/// Testes de integração para validar cache distribuído com Redis real
/// </summary>
public class CacheIntegrationTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public CacheIntegrationTests(WebApplicationFactoryFixture factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    /// <summary>
    /// Measures the performance of two identical HTTP requests and asserts the second is faster (cache hit).
    /// </summary>
    private async Task AssertCachePerformanceAsync(string endpoint, string operation = "cache operation")
    {
        // First call (cache miss)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await Client.GetAsync(endpoint);
        stopwatch1.Stop();

        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstCallTime = stopwatch1.ElapsedMilliseconds;

        // Second call (cache hit)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await Client.GetAsync(endpoint);
        stopwatch2.Stop();

        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCallTime = stopwatch2.ElapsedMilliseconds;

        // Assert - Second call should be faster or equal
        secondCallTime.Should().BeLessThanOrEqualTo(firstCallTime, $"{operation} should benefit from caching");

        // Assert - Data should be identical
        var data1 = await response1.Content.ReadAsStringAsync();
        var data2 = await response2.Content.ReadAsStringAsync();
        data1.Should().Be(data2);
    }

    /// <summary>
    /// Creates a test product via POST and returns the created product.
    /// </summary>
    private async Task<ProductResponseDto> CreateTestProductAsync(
        string name,
        Guid? id = null,
        string description = "Test Description",
        decimal price = 100m,
        decimal rating = 4.0m,
        string brand = "TestBrand",
        string color = "Red",
        string weight = "100g")
    {
        var createDto = new CreateProductDto
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = description,
            ImageUrl = $"https://example.com/{name.Replace(" ", "-").ToLower()}.jpg",
            Price = price,
            Rating = rating,
            Specifications = new ProductSpecificationsDto
            {
                Brand = brand,
                Color = color,
                Weight = weight
            }
        };

        var response = await Client.PostAsJsonAsync("/api/v1/products", createDto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ProductResponseDto>();
        created.Should().NotBeNull();
        return created!;
    }

    /// <summary>
    /// Gets the first product ID from the product list.
    /// </summary>
    private async Task<Guid> GetFirstProductIdAsync()
    {
        var response = await Client.GetAsync("/api/v1/products?page=1&pageSize=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        products.Should().NotBeNull();
        products!.Data.Should().NotBeEmpty();
        return products.Data.First().Id;
    }

    [Fact]
    public async Task GET_Products_SecondCall_ShouldUseCachedData()
    {
        // Act & Assert
        await AssertCachePerformanceAsync("/api/v1/products?page=1&pageSize=5", "product list endpoint");
    }

    [Fact]
    public async Task GET_ProductById_ShouldCacheIndividualProduct()
    {
        // Arrange - Get a valid product ID
        var productId = await GetFirstProductIdAsync();

        // Act & Assert
        await AssertCachePerformanceAsync($"/api/v1/products/{productId}", "product detail endpoint");
    }

    [Fact]
    public async Task POST_Product_ShouldInvalidateListCache()
    {
        // Arrange - Populate cache and verify product doesn't exist yet
        var initialResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var initialProducts = await initialResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();

        // Ensure the test product doesn't exist yet
        initialProducts!.Data.Should().NotContain(p => p.Name == "Cache Test Product XYZ Unique");

        // Act - Create new product (should invalidate cache)
        var createdProduct = await CreateTestProductAsync(
            name: "Cache Test Product XYZ Unique",
            description: "Testing cache invalidation",
            price: 999.99m,
            rating: 4.5m,
            brand: "CacheBrand",
            color: "Green",
            weight: "200g");

        // Assert - Next listing should have the new product (cache was invalidated)
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();

        // Verify the product was created and is in the list (proves cache was invalidated)
        products!.Data.Should().Contain(p => p.Name == "Cache Test Product XYZ Unique");
    }

    [Fact]
    public async Task PUT_Product_ShouldInvalidateCaches()
    {
        // Arrange - Create product and populate caches
        var created = await CreateTestProductAsync(
            name: "Original Cache Product",
            description: "Original description",
            brand: "CacheBrand",
            color: "White");

        // Populate individual cache
        await Client.GetAsync($"/api/v1/products/{created.Id}");

        // Populate list cache
        await Client.GetAsync("/api/v1/products?page=1&pageSize=100");

        // Act - Update product (should invalidate both caches)
        var updateDto = new UpdateProductDto
        {
            Name = "Updated Cache Product",
            Description = "Updated description",
            ImageUrl = "https://example.com/updated-cache.jpg",
            Price = 200m,
            Rating = 4.5m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "UpdatedBrand",
                Color = "Black",
                Weight = "150g"
            }
        };
        var updateResponse = await Client.PutAsJsonAsync($"/api/v1/products/{created.Id}", updateDto);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert - Individual fetch should return updated data
        var getResponse = await Client.GetAsync($"/api/v1/products/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        updated!.Name.Should().Be("Updated Cache Product");
        updated.Price.Should().Be(200m);

        // Assert - List should have updated product (fetch with max allowed pageSize)
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, "API should return OK");

        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        products.Should().NotBeNull("response should not be null");
        products!.Data.Should().NotBeEmpty("there should be products in the list");

        var productInList = products.Data.FirstOrDefault(p => p.Id == created.Id);
        productInList.Should().NotBeNull($"product with ID {created.Id} should be in the list");
        productInList!.Name.Should().Be("Updated Cache Product");
        productInList.Price.Should().Be(200m);
    }

    [Fact]
    public async Task DELETE_Product_ShouldInvalidateAllCaches()
    {
        // Arrange - Create product
        var created = await CreateTestProductAsync(
            name: "To Delete Cache",
            description: "Will be deleted for cache test",
            price: 50m,
            rating: 3.5m,
            brand: "DeleteBrand",
            color: "Gray",
            weight: "75g");

        // Populate caches
        await Client.GetAsync($"/api/v1/products/{created.Id}");
        var listBefore = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var productsBefore = await listBefore.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        var countBefore = productsBefore!.Pagination.TotalCount;

        // Act - Delete product (should invalidate caches)
        var deleteResponse = await Client.DeleteAsync($"/api/v1/products/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert - Product no longer exists
        var getResponse = await Client.GetAsync($"/api/v1/products/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Assert - List no longer contains the product
        var listAfter = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var productsAfter = await listAfter.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        productsAfter!.Pagination.TotalCount.Should().Be(countBefore - 1);
        productsAfter.Data.Should().NotContain(p => p.Id == created.Id);
    }

    [Fact]
    public async Task CompareProducts_ShouldCacheResults()
    {
        // Act & Assert - using the actual test GUIDs from test-products.csv
        await AssertCachePerformanceAsync("/api/v1/products/compare?ids=11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222,33333333-3333-3333-3333-333333333333", "product comparison endpoint");
    }
}

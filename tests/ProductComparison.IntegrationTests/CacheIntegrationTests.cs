using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Models;
using ProductComparison.IntegrationTests.DTOs;
using ProductComparison.IntegrationTests.Fixtures;
using Xunit;

namespace ProductComparison.IntegrationTests;

/// <summary>
/// Testes de integração para validar cache distribuído com Redis real
/// </summary>
public class CacheIntegrationTests : IntegrationTestBase
{
    public CacheIntegrationTests(WebApplicationFactoryFixture factory) : base(factory)
    {
    }
    
    [Fact]
    public async Task GET_Products_SecondCall_ShouldUseCachedData()
    {
        // Act 1 - Primeira chamada (cache miss - lê do CSV)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await Client.GetAsync("/api/v1/products?page=1&pageSize=5");
        stopwatch1.Stop();
        
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstCallTime = stopwatch1.ElapsedMilliseconds;
        
        // Act 2 - Segunda chamada (cache hit - lê do Redis)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await Client.GetAsync("/api/v1/products?page=1&pageSize=5");
        stopwatch2.Stop();
        
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCallTime = stopwatch2.ElapsedMilliseconds;
        
        // Assert - Segunda chamada deve ser mais rápida (cache hit)
        secondCallTime.Should().BeLessThan(firstCallTime);
        
        // Assert - Dados devem ser idênticos
        var data1 = await response1.Content.ReadAsStringAsync();
        var data2 = await response2.Content.ReadAsStringAsync();
        data1.Should().Be(data2);
    }
    
    [Fact]
    public async Task GET_ProductById_ShouldCacheIndividualProduct()
    {
        // Arrange - Pega um ID válido
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=1");
        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        var productId = products!.Data.First().Id;
        
        // Act 1 - Primeira busca (cache miss)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await Client.GetAsync($"/api/v1/products/{productId}");
        stopwatch1.Stop();
        
        // Act 2 - Segunda busca (cache hit)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await Client.GetAsync($"/api/v1/products/{productId}");
        stopwatch2.Stop();
        
        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Segunda chamada deve ser mais rápida
        stopwatch2.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(stopwatch1.ElapsedMilliseconds);
        
        // Dados idênticos
        var data1 = await response1.Content.ReadFromJsonAsync<ProductResponseDto>();
        var data2 = await response2.Content.ReadFromJsonAsync<ProductResponseDto>();
        data1.Should().BeEquivalentTo(data2);
    }
    
    [Fact]
    public async Task POST_Product_ShouldInvalidateListCache()
    {
        // Arrange - Popula cache da lista
        var initialResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var initialProducts = await initialResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        var initialCount = initialProducts!.Pagination.TotalCount;
        
        // Act - Cria novo produto (deve invalidar cache)
        var newProduct = new CreateProductDto
        {
            Name = "Cache Test Product",
            Description = "Testing cache invalidation",
            ImageUrl = "https://example.com/cache-test.jpg",
            Price = 999.99m,
            Rating = 4.5m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "CacheBrand",
                Color = "Green",
                Weight = "200g"
            }
        };
        
        var createResponse = await Client.PostAsJsonAsync("/api/v1/products", newProduct);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        // Assert - Próxima listagem deve ter o novo produto (cache foi invalidado)
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        
        products!.Pagination.TotalCount.Should().Be(initialCount + 1);
        products.Data.Should().Contain(p => p.Name == "Cache Test Product");
    }
    
    [Fact]
    public async Task PUT_Product_ShouldInvalidateCaches()
    {
        // Arrange - Cria produto e popula caches
        var createDto = new CreateProductDto
        {
            Name = "Original Cache Product",
            Description = "Original description",
            ImageUrl = "https://example.com/original-cache.jpg",
            Price = 100m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "CacheBrand",
                Color = "White",
                Weight = "100g"
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/products", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        
        // Popula cache individual
        await Client.GetAsync($"/api/v1/products/{created!.Id}");
        
        // Popula cache da lista
        await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        
        // Act - Atualiza produto (deve invalidar ambos os caches)
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
        
        // Assert - Busca individual deve retornar dados atualizados
        var getResponse = await Client.GetAsync($"/api/v1/products/{created.Id}");
        var updated = await getResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        updated!.Name.Should().Be("Updated Cache Product");
        updated.Price.Should().Be(200m);
        
        // Assert - Lista deve ter produto atualizado (busca com pageSize máximo permitido)
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a API deve retornar OK");
        
        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        products.Should().NotBeNull("a resposta não deve ser nula");
        products!.Data.Should().NotBeEmpty("deve haver produtos na lista");
        
        var productInList = products.Data.FirstOrDefault(p => p.Id == created.Id);
        productInList.Should().NotBeNull($"o produto com ID {created.Id} deve estar na lista");
        productInList!.Name.Should().Be("Updated Cache Product");
        productInList.Price.Should().Be(200m);
    }
    
    [Fact]
    public async Task DELETE_Product_ShouldInvalidateAllCaches()
    {
        // Arrange - Cria produto
        var createDto = new CreateProductDto
        {
            Name = "To Delete Cache",
            Description = "Will be deleted for cache test",
            ImageUrl = "https://example.com/delete-cache.jpg",
            Price = 50m,
            Rating = 3.5m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "DeleteBrand",
                Color = "Gray",
                Weight = "75g"
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/products", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        
        // Popula caches
        await Client.GetAsync($"/api/v1/products/{created!.Id}");
        var listBefore = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var productsBefore = await listBefore.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        var countBefore = productsBefore!.Pagination.TotalCount;
        
        // Act - Deleta produto (deve invalidar caches)
        var deleteResponse = await Client.DeleteAsync($"/api/v1/products/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Assert - Produto não existe mais
        var getResponse = await Client.GetAsync($"/api/v1/products/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        // Assert - Lista não contém mais o produto
        var listAfter = await Client.GetAsync("/api/v1/products?page=1&pageSize=100");
        var productsAfter = await listAfter.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        productsAfter!.Pagination.TotalCount.Should().Be(countBefore - 1);
        productsAfter.Data.Should().NotContain(p => p.Id == created.Id);
    }
    
    [Fact]
    public async Task CompareProducts_ShouldCacheResults()
    {
        // Act 1 - Primeira comparação (cache miss)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await Client.GetAsync("/api/v1/products/compare?ids=1,2,3");
        stopwatch1.Stop();
        
        // Act 2 - Segunda comparação (cache hit)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await Client.GetAsync("/api/v1/products/compare?ids=1,2,3");
        stopwatch2.Stop();
        
        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Segunda chamada mais rápida
        stopwatch2.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(stopwatch1.ElapsedMilliseconds);
        
        // Dados idênticos
        var data1 = await response1.Content.ReadFromJsonAsync<ProductComparisonDto>();
        var data2 = await response2.Content.ReadFromJsonAsync<ProductComparisonDto>();
        data1.Should().BeEquivalentTo(data2);
    }
}

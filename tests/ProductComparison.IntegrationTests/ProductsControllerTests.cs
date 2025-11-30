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
/// Testes de integração para os endpoints do ProductsController
/// </summary>
public class ProductsControllerTests : IntegrationTestBase
{
    public ProductsControllerTests(WebApplicationFactoryFixture factory) : base(factory)
    {
    }

    /// <summary>
    /// Creates a test product via POST and returns the created product.
    /// </summary>
    private async Task<ProductResponseDto> CreateTestProductAsync(
        string name = "Test Product",
        string description = "Test Description",
        string imageUrl = "https://example.com/test.jpg",
        decimal price = 100m,
        decimal rating = 4.0m,
        string brand = "TestBrand",
        string color = "Red",
        string weight = "100g")
    {
        var createDto = new CreateProductDto
        {
            Name = name,
            Description = description,
            ImageUrl = imageUrl,
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
    private async Task<int> GetFirstProductIdAsync()
    {
        var response = await Client.GetAsync("/api/v1/products?page=1&pageSize=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var products = await response.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        products.Should().NotBeNull();
        products!.Data.Should().NotBeEmpty();
        return products.Data.First().Id;
    }
    
    [Fact]
    public async Task GET_Products_ReturnsSuccessAndProducts()
    {
        // Act - Requisição HTTP REAL
        var response = await Client.GetAsync("/api/v1/products?page=1&pageSize=10");
        
        // Assert - Status code
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Assert - Content type
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        // Assert - Deserialização JSON  
        var result = await response.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        
        result.Should().NotBeNull();
        result!.Data.Should().NotBeEmpty();
        result.Data.Should().HaveCountGreaterOrEqualTo(5); // test-products.csv tem pelo menos 5 produtos
        result.Pagination.TotalCount.Should().BeGreaterOrEqualTo(5);
        result.Pagination.Page.Should().Be(1);
        result.Pagination.PageSize.Should().Be(10);
        
        // Assert - Estrutura do produto
        var firstProduct = result.Data.First();
        firstProduct.Id.Should().BeGreaterThan(0);
        firstProduct.Name.Should().NotBeNullOrEmpty();
        firstProduct.Price.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public async Task GET_Products_WithPagination_ReturnsCorrectPage()
    {
        // Act - Solicita página 1 com tamanho 2
        var response = await Client.GetAsync("/api/v1/products?page=1&pageSize=2");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        result!.Data.Should().HaveCount(2); // Solicitou pageSize=2
        result.Pagination.TotalCount.Should().BeGreaterOrEqualTo(5); // Pelo menos 5 produtos no total
        result.Pagination.Page.Should().Be(1);
        result.Pagination.PageSize.Should().Be(2);
        result.Pagination.TotalPages.Should().BeGreaterOrEqualTo(3); // Com 5+ items e pageSize=2, pelo menos 3 páginas
    }
    
    [Fact]
    public async Task GET_ProductById_ReturnsProduct()
    {
        // Arrange - Get a valid product ID
        var productId = await GetFirstProductIdAsync();
        
        // Act
        var response = await Client.GetAsync($"/api/v1/products/{productId}");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var product = await response.Content.ReadFromJsonAsync<ProductResponseDto>();
        product.Should().NotBeNull();
        product!.Id.Should().Be(productId);
        product.Name.Should().NotBeNullOrEmpty();
    }
    
    [Fact]
    public async Task GET_ProductById_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await Client.GetAsync("/api/v1/products/99999");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    
    [Fact]
    public async Task GET_CompareProducts_ReturnsComparison()
    {
        // Act - Compara os 3 primeiros produtos
        var response = await Client.GetAsync("/api/v1/products/compare?ids=1,2,3");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ProductComparisonDto>();
        result.Should().NotBeNull();
        result!.Products.Should().HaveCount(3);
        result.Differences.Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task GET_CompareProducts_WithInvalidIds_ReturnsBadRequest()
    {
        // Act - IDs inválidos
        var response = await Client.GetAsync("/api/v1/products/compare?ids=invalid");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    
    [Fact]
    public async Task POST_Product_CreatesNewProduct()
    {
        // Act
        var createdProduct = await CreateTestProductAsync(
            name: "Test Product Integration",
            description: "Created by integration test",
            price: 999.99m,
            rating: 4.5m,
            color: "Blue",
            weight: "500g");

        // Assert
        createdProduct.Name.Should().Be("Test Product Integration");
        createdProduct.Price.Should().Be(999.99m);
    }
    
    [Fact]
    public async Task POST_Product_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange - Price negativo (inválido)
        var invalidProduct = new CreateProductDto
        {
            Name = "Invalid",
            Description = "Invalid Description",
            ImageUrl = "https://example.com/invalid.jpg",
            Price = -100m, // INVÁLIDO
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Test",
                Color = "Red",
                Weight = "100g"
            }
        };
        
        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/products", invalidProduct);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
    
    [Fact]
    public async Task PUT_Product_UpdatesExistingProduct()
    {
        // Arrange - Create product first
        var created = await CreateTestProductAsync(
            name: "Original Name",
            description: "Original Description",
            imageUrl: "https://example.com/original.jpg",
            brand: "OriginalBrand");
        
        // Act - Update the product
        var updateDto = new UpdateProductDto
        {
            Name = "Updated Name",
            Description = "Updated Description",
            ImageUrl = "https://example.com/updated.jpg",
            Price = 200m,
            Rating = 4.5m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "UpdatedBrand",
                Color = "Blue",
                Weight = "150g"
            }
        };
        var response = await Client.PutAsJsonAsync($"/api/v1/products/{created.Id}", updateDto);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var updated = await response.Content.ReadFromJsonAsync<ProductResponseDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Name");
        updated.Price.Should().Be(200m);
    }
    
    [Fact]
    public async Task PUT_Product_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var updateDto = new UpdateProductDto
        {
            Name = "Test Update",
            Description = "Test Description",
            ImageUrl = "https://example.com/test.jpg",
            Price = 100m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Test",
                Color = "Red",
                Weight = "100g"
            }
        };
        
        // Act
        var response = await Client.PutAsJsonAsync("/api/v1/products/99999", updateDto);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    
    [Fact]
    public async Task DELETE_Product_RemovesProduct()
    {
        // Arrange - Create product first
        var created = await CreateTestProductAsync(
            name: "To Delete",
            description: "Will be deleted",
            imageUrl: "https://example.com/delete.jpg",
            brand: "DeleteBrand",
            color: "Black",
            weight: "50g");
        
        // Act - Delete the product
        var response = await Client.DeleteAsync($"/api/v1/products/{created.Id}");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify product was removed
        var getResponse = await Client.GetAsync($"/api/v1/products/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
    
    [Fact]
    public async Task DELETE_Product_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await Client.DeleteAsync("/api/v1/products/99999");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

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
        // Arrange - Primeiro pega lista para ter um ID válido
        var listResponse = await Client.GetAsync("/api/v1/products?page=1&pageSize=1");
        var products = await listResponse.Content.ReadFromJsonAsync<ApiPagedResponse<ProductResponseDto>>();
        var productId = products!.Data.First().Id;
        
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
        // Arrange
        var newProduct = new CreateProductDto
        {
            Name = "Test Product Integration",
            Description = "Created by integration test",
            ImageUrl = "https://example.com/test.jpg",
            Price = 999.99m,
            Rating = 4.5m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "TestBrand",
                Color = "Blue",
                Weight = "500g"
            }
        };
        
        // Act
        var response = await Client.PostAsJsonAsync("/api/v1/products", newProduct);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var createdProduct = await response.Content.ReadFromJsonAsync<ProductResponseDto>();
        createdProduct.Should().NotBeNull();
        createdProduct!.Name.Should().Be(newProduct.Name);
        createdProduct.Price.Should().Be(newProduct.Price);
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
        // Arrange - Cria produto primeiro
        var createDto = new CreateProductDto
        {
            Name = "Original Name",
            Description = "Original Description",
            ImageUrl = "https://example.com/original.jpg",
            Price = 100m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "OriginalBrand",
                Color = "Red",
                Weight = "100g"
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/products", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        
        // Act - Atualiza o produto
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
        var response = await Client.PutAsJsonAsync($"/api/v1/products/{created!.Id}", updateDto);
        
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
        // Arrange - Cria produto primeiro
        var createDto = new CreateProductDto
        {
            Name = "To Delete",
            Description = "Will be deleted",
            ImageUrl = "https://example.com/delete.jpg",
            Price = 100m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "DeleteBrand",
                Color = "Black",
                Weight = "50g"
            }
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/products", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<ProductResponseDto>();
        
        // Act - Deleta o produto
        var response = await Client.DeleteAsync($"/api/v1/products/{created!.Id}");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verifica que produto foi removido
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

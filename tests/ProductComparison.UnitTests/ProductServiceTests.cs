using Microsoft.Extensions.Logging;
using Moq;
using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Domain.Services;
using ProductComparison.Domain.ValueObjects;

namespace ProductComparison.UnitTests;

public class ProductServiceTests
{
    private readonly Mock<IProductRepository> _mockRepository;
    private readonly Mock<ILogger<ProductService>> _mockLogger;
    private readonly Mock<ICacheService> _mockCache;
    private readonly ProductService _productService;

    public ProductServiceTests()
    {
        _mockRepository = new Mock<IProductRepository>();
        _mockLogger = new Mock<ILogger<ProductService>>();
        _mockCache = new Mock<ICacheService>();
        _productService = new ProductService(_mockRepository.Object, _mockLogger.Object, _mockCache.Object);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllProducts_WhenProductsExist()
    {
        // Arrange
        var products = new List<Product>
        {
            new Product(
                id: 1,
                name: "Product 1",
                description: "Description 1",
                imageUrl: "http://example.com/image1.jpg",
                price: new Price(100.00m),
                rating: new Rating(4.0m, 10),
                specifications: new ProductSpecifications("Brand A", "Red", "1kg")
            ),
            new Product(
                id: 2,
                name: "Product 2",
                description: "Description 2",
                imageUrl: "http://example.com/image2.jpg",
                price: new Price(200.00m),
                rating: new Rating(4.0m, 5),
                specifications: new ProductSpecifications("Brand B", "Blue", "2kg")
            )
        };

        _mockRepository.Setup(repo => repo.GetAllAsync())
            .ReturnsAsync(products);

        // Act
        var result = await _productService.GetAllAsync(page: 1, pageSize: 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count());
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal("Product 1", result.Items.First().Name);
        Assert.Equal(100.00m, result.Items.First().Price);
        Assert.Equal("Product 2", result.Items.Last().Name);
        Assert.Equal(200.00m, result.Items.Last().Price);

        _mockRepository.Verify(repo => repo.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnEmptyList_WhenNoProductsExist()
    {
        // Arrange
        var emptyProducts = new List<Product>();

        _mockRepository.Setup(repo => repo.GetAllAsync())
            .ReturnsAsync(emptyProducts);

        // Act
        var result = await _productService.GetAllAsync(page: 1, pageSize: 10);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);

        _mockRepository.Verify(repo => repo.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnProduct_WhenProductExists()
    {
        // Arrange
        var productId = 1;
        var product = new Product(
            id: productId,
            name: "Test Product",
            description: "Test Description",
            imageUrl: "http://example.com/test.jpg",
            price: new Price(150.00m),
            rating: new Rating(4.5m, 20),
            specifications: new ProductSpecifications("Test Brand", "Green", "1.5kg")
        );

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(product);

        // Act
        var result = await _productService.GetByIdAsync(productId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(productId, result.Id);
        Assert.Equal("Test Product", result.Name);
        Assert.Equal("Test Description", result.Description);
        Assert.Equal(150.00m, result.Price);
        Assert.Equal(4.5m, result.Rating);

        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
    }

    [Fact]
    public async Task GetByIdAsync_ShouldThrowProductNotFoundException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = 999;

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync((Product?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => _productService.GetByIdAsync(productId)
        );

        Assert.NotNull(exception);
        Assert.Contains($"Product with ID {productId}", exception.Message);
        Assert.Equal(404, exception.StatusCode);
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
    }

    [Fact]
    public async Task CompareAsync_ShouldReturnComparison_WhenProductsExist()
    {
        // Arrange
        var product1 = new Product(
            id: 1,
            name: "Product 1",
            description: "Description 1",
            imageUrl: "http://example.com/image1.jpg",
            price: new Price(100.00m),
            rating: new Rating(4.0m, 10),
            specifications: new ProductSpecifications("Brand A", "Red", "1kg")
        );

        var product2 = new Product(
            id: 2,
            name: "Product 2",
            description: "Description 2",
            imageUrl: "http://example.com/image2.jpg",
            price: new Price(200.00m),
            rating: new Rating(4.5m, 5),
            specifications: new ProductSpecifications("Brand B", "Blue", "2kg")
        );

        _mockRepository.Setup(repo => repo.GetByIdAsync(1))
            .ReturnsAsync(product1);
        _mockRepository.Setup(repo => repo.GetByIdAsync(2))
            .ReturnsAsync(product2);

        // Act
        var result = await _productService.CompareAsync("1,2");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Products);
        Assert.Equal(2, result.Products.Count);
        Assert.Equal("Product 1", result.Products[0].Name);
        Assert.Equal("Product 2", result.Products[1].Name);
        Assert.NotNull(result.Differences);
        Assert.Contains(result.Differences, d => d.Contains("Price difference"));
        Assert.Contains(result.Differences, d => d.Contains("Average price"));

        _mockRepository.Verify(repo => repo.GetByIdAsync(1), Times.Once);
        _mockRepository.Verify(repo => repo.GetByIdAsync(2), Times.Once);
    }

    [Fact]
    public async Task CompareAsync_ShouldThrowProductValidationException_WhenProductIdsIsEmpty()
    {
        // Arrange
        var emptyProductIds = "";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ProductValidationException>(
            () => _productService.CompareAsync(emptyProductIds)
        );

        Assert.NotNull(exception);
        Assert.Contains("No product IDs provided", exception.Message);
        Assert.Equal(400, exception.StatusCode);
        _mockRepository.Verify(repo => repo.GetByIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateProduct_WhenDataIsValid()
    {
        // Arrange
        var createDto = new CreateProductDto
        {
            Name = "New Product",
            Description = "New Description",
            ImageUrl = "http://example.com/new.jpg",
            Price = 250.00m,
            Rating = 4.8m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "New Brand",
                Color = "Yellow",
                Weight = "3kg"
            }
        };

        var createdProduct = new Product(
            id: 10,
            name: createDto.Name,
            description: createDto.Description,
            imageUrl: createDto.ImageUrl,
            price: new Price(createDto.Price),
            rating: new Rating(createDto.Rating, 1),
            specifications: new ProductSpecifications(
                createDto.Specifications.Brand,
                createDto.Specifications.Color,
                createDto.Specifications.Weight
            )
        );

        _mockRepository.Setup(repo => repo.CreateAsync(It.IsAny<Product>()))
            .ReturnsAsync(createdProduct);

        // Act
        var result = await _productService.CreateAsync(createDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10, result.Id);
        Assert.Equal("New Product", result.Name);
        Assert.Equal("New Description", result.Description);
        Assert.Equal(250.00m, result.Price);
        Assert.Equal(4.8m, result.Rating);
        Assert.Equal("New Brand", result.Specifications.Brand);

        _mockRepository.Verify(repo => repo.CreateAsync(It.IsAny<Product>()), Times.Once);
        _mockCache.Verify(cache => cache.RemoveByPatternAsync("products:list:*"), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowArgumentException_WhenPriceIsNegative()
    {
        // Arrange
        var createDto = new CreateProductDto
        {
            Name = "Invalid Product",
            Description = "Description",
            ImageUrl = "http://example.com/invalid.jpg",
            Price = -10.00m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Brand",
                Color = "Color",
                Weight = "1kg"
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _productService.CreateAsync(createDto)
        );

        Assert.NotNull(exception);
        Assert.Contains("Price cannot be negative", exception.Message);
        _mockRepository.Verify(repo => repo.CreateAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrowArgumentException_WhenRatingIsOutOfRange()
    {
        // Arrange
        var createDto = new CreateProductDto
        {
            Name = "Invalid Product",
            Description = "Description",
            ImageUrl = "http://example.com/invalid.jpg",
            Price = 100.00m,
            Rating = 6.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Brand",
                Color = "Color",
                Weight = "1kg"
            }
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _productService.CreateAsync(createDto)
        );

        Assert.NotNull(exception);
        Assert.Contains("Rating must be between 0 and 5", exception.Message);
        _mockRepository.Verify(repo => repo.CreateAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpdateProduct_WhenDataIsValid()
    {
        // Arrange
        var productId = 5;
        var existingProduct = new Product(
            id: productId,
            name: "Old Product",
            description: "Old Description",
            imageUrl: "http://example.com/old.jpg",
            price: new Price(100.00m),
            rating: new Rating(3.5m, 15),
            specifications: new ProductSpecifications("Old Brand", "Red", "2kg")
        );

        var updateDto = new UpdateProductDto
        {
            Name = "Updated Product",
            Description = "Updated Description",
            ImageUrl = "http://example.com/updated.jpg",
            Price = 150.00m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Updated Brand",
                Color = "Blue",
                Weight = "2.5kg"
            }
        };

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(existingProduct);
        _mockRepository.Setup(repo => repo.UpdateAsync(It.IsAny<Product>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _productService.UpdateAsync(productId, updateDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(productId, result.Id);
        Assert.Equal("Updated Product", result.Name);
        Assert.Equal("Updated Description", result.Description);
        Assert.Equal(150.00m, result.Price);
        Assert.Equal("Updated Brand", result.Specifications.Brand);
        Assert.Equal("Blue", result.Specifications.Color);
        Assert.Equal("2.5kg", result.Specifications.Weight);

        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<Product>()), Times.Once);
        _mockCache.Verify(cache => cache.RemoveAsync($"products:details:{productId}"), Times.Once);
        _mockCache.Verify(cache => cache.RemoveByPatternAsync("products:list:*"), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowProductNotFoundException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = 999;
        var updateDto = new UpdateProductDto
        {
            Name = "Updated Product",
            Description = "Updated Description",
            ImageUrl = "http://example.com/updated.jpg",
            Price = 150.00m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Brand",
                Color = "Color",
                Weight = "1kg"
            }
        };

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync((Product?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => _productService.UpdateAsync(productId, updateDto)
        );

        Assert.NotNull(exception);
        Assert.Contains($"Product with ID {productId}", exception.Message);
        Assert.Equal(404, exception.StatusCode);
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowArgumentException_WhenPriceIsNegative()
    {
        // Arrange
        var productId = 5;
        var existingProduct = new Product(
            id: productId,
            name: "Existing Product",
            description: "Description",
            imageUrl: "http://example.com/existing.jpg",
            price: new Price(100.00m),
            rating: new Rating(3.5m, 15),
            specifications: new ProductSpecifications("Brand", "Red", "2kg")
        );

        var updateDto = new UpdateProductDto
        {
            Name = "Updated Product",
            Description = "Updated Description",
            ImageUrl = "http://example.com/updated.jpg",
            Price = -50.00m,
            Rating = 4.0m,
            Specifications = new ProductSpecificationsDto
            {
                Brand = "Brand",
                Color = "Blue",
                Weight = "2kg"
            }
        };

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(existingProduct);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _productService.UpdateAsync(productId, updateDto)
        );

        Assert.NotNull(exception);
        Assert.Contains("Price cannot be negative", exception.Message);
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<Product>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAsync_ShouldDeleteProduct_WhenProductExists()
    {
        // Arrange
        var productId = 7;
        var existingProduct = new Product(
            id: productId,
            name: "Product to Delete",
            description: "Description",
            imageUrl: "http://example.com/delete.jpg",
            price: new Price(100.00m),
            rating: new Rating(4.0m, 10),
            specifications: new ProductSpecifications("Brand", "Red", "1kg")
        );

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(existingProduct);
        _mockRepository.Setup(repo => repo.DeleteAsync(productId))
            .Returns(Task.CompletedTask);

        // Act
        await _productService.DeleteAsync(productId);

        // Assert
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.DeleteAsync(productId), Times.Once);
        _mockCache.Verify(cache => cache.RemoveAsync($"products:details:{productId}"), Times.Once);
        _mockCache.Verify(cache => cache.RemoveByPatternAsync("products:list:*"), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldThrowProductNotFoundException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = 999;

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync((Product?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ProductNotFoundException>(
            () => _productService.DeleteAsync(productId)
        );

        Assert.NotNull(exception);
        Assert.Contains($"Product with ID {productId}", exception.Message);
        Assert.Equal(404, exception.StatusCode);
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.DeleteAsync(It.IsAny<int>()), Times.Never);
    }
}

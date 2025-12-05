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
    private readonly Mock<IProductAuditLogRepository> _mockAuditRepository;
    private readonly Mock<ILogger<ProductService>> _mockLogger;
    private readonly Mock<ICacheService> _mockCache;
    private readonly ProductService _productService;

    public ProductServiceTests()
    {
        _mockRepository = new Mock<IProductRepository>();
        _mockAuditRepository = new Mock<IProductAuditLogRepository>();
        _mockLogger = new Mock<ILogger<ProductService>>();
        _mockCache = new Mock<ICacheService>();
        _productService = new ProductService(_mockRepository.Object, _mockAuditRepository.Object, _mockLogger.Object, _mockCache.Object);
    }

    /// <summary>
    /// Creates a test product with the specified properties.
    /// </summary>
    private Product CreateTestProduct(
        Guid? id = null,
        string name = "Test Product",
        string description = "Test Description",
        string imageUrl = "http://example.com/test.jpg",
        decimal price = 100.00m,
        decimal rating = 4.0m,
        int reviewCount = 10,
        string brand = "Test Brand",
        string color = "Red",
        string weight = "1kg",
        int version = 0)
    {
        return new Product(
            id: id ?? Guid.NewGuid(),
            name: name,
            description: description,
            imageUrl: imageUrl,
            price: new Price(price),
            rating: new Rating(rating, reviewCount),
            specifications: new ProductSpecifications(brand, color, weight),
            version: version
        );
    }

    /// <summary>
    /// Creates a test CreateProductDto with the specified properties.
    /// </summary>
    private CreateProductDto CreateTestCreateDto(
        Guid? id = null,
        string name = "Test Product",
        string description = "Test Description",
        string imageUrl = "http://example.com/test.jpg",
        decimal price = 100.00m,
        decimal rating = 4.0m,
        string brand = "Test Brand",
        string color = "Red",
        string weight = "1kg")
    {
        return new CreateProductDto
        {
            Id = id ?? Guid.NewGuid(),
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
    }

    /// <summary>
    /// Creates a test UpdateProductDto with the specified properties.
    /// </summary>
    private UpdateProductDto CreateTestUpdateDto(
        int version = 0,
        string name = "Updated Product",
        string description = "Updated Description",
        string imageUrl = "http://example.com/updated.jpg",
        decimal price = 150.00m,
        decimal rating = 4.5m,
        string brand = "Updated Brand",
        string color = "Blue",
        string weight = "2kg")
    {
        return new UpdateProductDto
        {
            Version = version,
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
    }

    /// <summary>
    /// Verifies cache invalidation for product operations.
    /// </summary>
    private void VerifyCacheInvalidation(Guid productId, bool includeList = true)
    {
        _mockCache.Verify(cache => cache.RemoveAsync($"products:details:{productId}"), Times.Once);
        if (includeList)
        {
            _mockCache.Verify(cache => cache.RemoveByPatternAsync("products:list:*"), Times.Once);
        }
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnAllProducts_WhenProductsExist()
    {
        // Arrange
        var products = new List<Product>
        {
            CreateTestProduct(id: Guid.NewGuid(), name: "Product 1", description: "Description 1", imageUrl: "http://example.com/image1.jpg", price: 100.00m, brand: "Brand A"),
            CreateTestProduct(id: Guid.NewGuid(), name: "Product 2", description: "Description 2", imageUrl: "http://example.com/image2.jpg", price: 200.00m, brand: "Brand B", color: "Blue", weight: "2kg")
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
        var productId = Guid.NewGuid();
        var product = CreateTestProduct(id: productId, price: 150.00m, rating: 4.5m, reviewCount: 20, color: "Green", weight: "1.5kg");

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
        var productId = Guid.NewGuid();

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
        var product1 = CreateTestProduct(id: Guid.NewGuid(), name: "Product 1", description: "Description 1", imageUrl: "http://example.com/image1.jpg", price: 100.00m, brand: "Brand A");
        var product2 = CreateTestProduct(id: Guid.NewGuid(), name: "Product 2", description: "Description 2", imageUrl: "http://example.com/image2.jpg", price: 200.00m, rating: 4.5m, reviewCount: 5, brand: "Brand B", color: "Blue", weight: "2kg");

        _mockRepository.Setup(repo => repo.GetByIdAsync(product1.Id))
            .ReturnsAsync(product1);
        _mockRepository.Setup(repo => repo.GetByIdAsync(product2.Id))
            .ReturnsAsync(product2);

        // Act
        var result = await _productService.CompareAsync($"{product1.Id},{product2.Id}");

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Products);
        Assert.Equal(2, result.Products.Count);
        Assert.Equal("Product 1", result.Products[0].Name);
        Assert.Equal("Product 2", result.Products[1].Name);
        Assert.NotNull(result.Differences);
        Assert.Contains(result.Differences, d => d.Contains("Price difference"));
        Assert.Contains(result.Differences, d => d.Contains("Average price"));

        _mockRepository.Verify(repo => repo.GetByIdAsync(product1.Id), Times.Once);
        _mockRepository.Verify(repo => repo.GetByIdAsync(product2.Id), Times.Once);
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
        _mockRepository.Verify(repo => repo.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_ShouldCreateProduct_WhenDataIsValid()
    {
        // Arrange
        var createDto = CreateTestCreateDto(name: "New Product", description: "New Description", imageUrl: "http://example.com/new.jpg", price: 250.00m, rating: 4.8m, brand: "New Brand", color: "Yellow", weight: "3kg");
        var createdProduct = CreateTestProduct(id: createDto.Id, name: createDto.Name, description: createDto.Description, imageUrl: createDto.ImageUrl, price: createDto.Price, rating: createDto.Rating, reviewCount: 1, brand: createDto.Specifications.Brand, color: createDto.Specifications.Color, weight: createDto.Specifications.Weight);

        _mockRepository.Setup(repo => repo.CreateAsync(It.IsAny<Product>()))
            .ReturnsAsync(createdProduct);

        // Act
        var result = await _productService.CreateAsync(createDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(createDto.Id, result.Id);
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
        var createDto = CreateTestCreateDto(name: "Invalid Product", imageUrl: "http://example.com/invalid.jpg", price: -10.00m);

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
        var createDto = CreateTestCreateDto(name: "Invalid Product", imageUrl: "http://example.com/invalid.jpg", rating: 6.0m);

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
        var productId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(id: productId, name: "Old Product", description: "Old Description", imageUrl: "http://example.com/old.jpg", rating: 3.5m, reviewCount: 15, brand: "Old Brand", weight: "2kg");
        var updateDto = CreateTestUpdateDto(price: 150.00m, rating: 4.0m, weight: "2.5kg");

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
        VerifyCacheInvalidation(productId);
    }

    [Fact]
    public async Task UpdateAsync_ShouldThrowProductNotFoundException_WhenProductDoesNotExist()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var updateDto = CreateTestUpdateDto();

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
        var productId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(id: productId, name: "Existing Product", imageUrl: "http://example.com/existing.jpg", rating: 3.5m, reviewCount: 15, weight: "2kg");
        var updateDto = CreateTestUpdateDto(price: -50.00m, weight: "2kg");

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
    public async Task UpdateAsync_ShouldThrowConcurrencyException_WhenVersionMismatches()
    {
        // Arrange - Optimistic concurrency control test
        var productId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(id: productId, name: "Current Product", description: "Current Description", imageUrl: "http://example.com/current.jpg", rating: 3.5m, reviewCount: 15, weight: "2kg", version: 5);
        var updateDto = CreateTestUpdateDto(version: 3, price: 150.00m, weight: "2.5kg"); // Wrong version

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(existingProduct);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConcurrencyException>(
            () => _productService.UpdateAsync(productId, updateDto)
        );

        Assert.NotNull(exception);
        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(409, exception.StatusCode);
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.UpdateAsync(It.IsAny<Product>()), Times.Never);
    }
    [Fact]
    public async Task DeleteAsync_ShouldDeleteProduct_WhenProductExists()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var existingProduct = CreateTestProduct(id: productId, name: "Product to Delete", imageUrl: "http://example.com/delete.jpg");

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync(existingProduct);
        _mockRepository.Setup(repo => repo.DeleteAsync(productId))
            .Returns(Task.CompletedTask);

        // Act
        await _productService.DeleteAsync(productId);

        // Assert
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.DeleteAsync(productId), Times.Once);
        VerifyCacheInvalidation(productId);
    }

    [Fact]
    public async Task DeleteAsync_ShouldCompleteSuccessfully_WhenProductDoesNotExist()
    {
        // Arrange - Idempotent DELETE should not throw on non-existent product (RFC 9110)
        var productId = Guid.NewGuid();

        _mockRepository.Setup(repo => repo.GetByIdAsync(productId))
            .ReturnsAsync((Product?)null);

        // Act - Should complete without throwing
        await _productService.DeleteAsync(productId);

        // Assert
        _mockRepository.Verify(repo => repo.GetByIdAsync(productId), Times.Once);
        _mockRepository.Verify(repo => repo.DeleteAsync(It.IsAny<Guid>()), Times.Never);
        // No exception thrown - DELETE is idempotent
    }
}

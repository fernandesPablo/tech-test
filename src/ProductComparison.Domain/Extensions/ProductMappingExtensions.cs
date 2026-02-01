using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Entities;
using ProductComparison.Domain.ValueObjects;

namespace ProductComparison.Domain.Extensions;

/// <summary>
/// Extension methods for mapping between domain entities and DTOs.
/// Centralizes mapping logic to follow DRY principle.
/// </summary>
public static class ProductMappingExtensions
{
    /// <summary>
    /// Converts a Product entity to a ProductResponseDto.
    /// </summary>
    public static ProductResponseDto ToDto(this Product product) => new()
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

    /// <summary>
    /// Converts a CreateProductDto to a Product entity.
    /// </summary>
    public static Product ToEntity(this CreateProductDto dto) => new(
        id: dto.Id,
        name: dto.Name,
        description: dto.Description,
        imageUrl: dto.ImageUrl,
        price: new Price(dto.Price),
        rating: new Rating(dto.Rating, 1), // Assume 1 initial rating
        specifications: new ProductSpecifications(
            dto.Specifications.Brand,
            dto.Specifications.Color,
            dto.Specifications.Weight
        )
    );
}
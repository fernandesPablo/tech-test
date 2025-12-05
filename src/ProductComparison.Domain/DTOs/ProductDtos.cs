using System.ComponentModel.DataAnnotations;

namespace ProductComparison.Domain.DTOs;

public record ProductResponseDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = null!;
    public string Description { get; init; } = null!;
    public string ImageUrl { get; init; } = null!;
    public decimal Price { get; init; }
    public decimal Rating { get; init; }
    public ProductSpecificationsDto Specifications { get; init; } = null!;
    public int Version { get; init; }
}

public record CreateProductDto
{
    [Required(ErrorMessage = "Id is required")]
    public Guid Id { get; init; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 200 characters")]
    public string Name { get; init; } = null!;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000, MinimumLength = 6, ErrorMessage = "Description must be between 6 and 1000 characters")]
    public string Description { get; init; } = null!;

    [Required(ErrorMessage = "ImageUrl is required")]
    [Url(ErrorMessage = "ImageUrl must be a valid URL")]
    [StringLength(500, ErrorMessage = "ImageUrl cannot exceed 500 characters")]
    public string ImageUrl { get; init; } = null!;

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 1000000, ErrorMessage = "Price must be between 0.01 and 1,000,000")]
    public decimal Price { get; init; }

    [Required(ErrorMessage = "Rating is required")]
    [Range(0, 5, ErrorMessage = "Rating must be between 0 and 5")]
    public decimal Rating { get; init; }

    [Required(ErrorMessage = "Specifications are required")]
    public ProductSpecificationsDto Specifications { get; init; } = null!;
}

public record UpdateProductDto
{
    [Required(ErrorMessage = "Version is required for optimistic concurrency control")]
    [Range(0, int.MaxValue, ErrorMessage = "Version must be a non-negative integer")]
    public int Version { get; init; }

    [Required(ErrorMessage = "Name is required")]
    [StringLength(200, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 200 characters")]
    public string Name { get; init; } = null!;

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000, MinimumLength = 6, ErrorMessage = "Description must be between 6 and 1000 characters")]
    public string Description { get; init; } = null!;

    [Required(ErrorMessage = "ImageUrl is required")]
    [Url(ErrorMessage = "ImageUrl must be a valid URL")]
    [StringLength(500, ErrorMessage = "ImageUrl cannot exceed 500 characters")]
    public string ImageUrl { get; init; } = null!;

    [Required(ErrorMessage = "Price is required")]
    [Range(0.01, 1000000, ErrorMessage = "Price must be between 0.01 and 1,000,000")]
    public decimal Price { get; init; }

    [Required(ErrorMessage = "Rating is required")]
    [Range(0, 5, ErrorMessage = "Rating must be between 0 and 5")]
    public decimal Rating { get; init; }

    [Required(ErrorMessage = "Specifications are required")]
    public ProductSpecificationsDto Specifications { get; init; } = null!;
}

public record ProductSpecificationsDto
{
    [Required(ErrorMessage = "Brand is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Brand must be between 2 and 100 characters")]
    public string Brand { get; init; } = null!;

    [Required(ErrorMessage = "Color is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Color must be between 2 and 50 characters")]
    public string Color { get; init; } = null!;

    [Required(ErrorMessage = "Weight is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Weight must be between 2 and 50 characters")]
    [RegularExpression(@"^\d+(\.\d+)?\s*(kg|g|lbs|oz)$", ErrorMessage = "Weight must be in format: number + unit (kg, g, lbs, oz)")]
    public string Weight { get; init; } = null!;
}

public record ProductComparisonDto
{
    public List<ProductResponseDto> Products { get; init; } = new();
    public List<string> Differences { get; init; } = new();
}
namespace ProductComparison.Domain.Models;

public class ProductUpdateModel
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ImageUrl { get; init; }
    public decimal Price { get; init; }
    public decimal Rating { get; init; }
    public ProductSpecificationsUpdateModel Specifications { get; init; } = null!;
}

public class ProductSpecificationsUpdateModel
{
    public string? Brand { get; init; }
    public string? Color { get; init; }
    public string? Weight { get; init; }
}
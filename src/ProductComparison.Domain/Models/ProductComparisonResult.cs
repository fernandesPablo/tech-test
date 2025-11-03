using ProductComparison.Domain.Entities;

namespace ProductComparison.Domain.Models;

public class ProductComparisonResult
{
    public IEnumerable<Product> Products { get; set; } = new List<Product>();
    public decimal PriceDifference { get; init; }
    public decimal AveragePrice { get; init; }
}
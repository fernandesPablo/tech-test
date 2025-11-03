namespace ProductComparison.Domain.ValueObjects;

public record ProductSpecifications
{
    public string Brand { get; }
    public string Color { get; }
    public string Weight { get; }

    public ProductSpecifications(
        string brand,
        string color,
        string weight)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            throw new ArgumentException("Brand cannot be empty", nameof(brand));
        }

        if (string.IsNullOrWhiteSpace(color))
        {
            throw new ArgumentException("Color cannot be empty", nameof(color));
        }

        if (string.IsNullOrWhiteSpace(weight))
        {
            throw new ArgumentException("Weight cannot be empty", nameof(weight));
        }

        Brand = brand;
        Color = color;
        Weight = weight;
    }
}
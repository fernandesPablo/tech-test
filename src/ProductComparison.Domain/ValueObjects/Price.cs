namespace ProductComparison.Domain.ValueObjects;

public record Price
{
    public decimal Value { get; }
    public string Currency { get; }

    public Price(decimal value, string currency = "Real")
    {
        if (value < 0)
            throw new ArgumentException("Price cannot be negative", nameof(value));

        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency cannot be empty", nameof(currency));

        Value = value;
        Currency = currency;
    }

    public override string ToString() => $"{Currency} {Value:F2}";
}
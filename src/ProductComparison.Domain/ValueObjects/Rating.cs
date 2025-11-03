namespace ProductComparison.Domain.ValueObjects;

public record Rating
{
    public decimal Value { get; }
    public int NumberOfRatings { get; }

    public Rating(decimal value, int numberOfRatings)
    {
        if (value < 0 || value > 5)
            throw new ArgumentException("Rating must be between 0 and 5", nameof(value));

        if (numberOfRatings < 0)
            throw new ArgumentException("Number of ratings cannot be negative", nameof(numberOfRatings));

        Value = value;
        NumberOfRatings = numberOfRatings;
    }

    public override string ToString() => $"{Value:F1} ({NumberOfRatings} ratings)";
}
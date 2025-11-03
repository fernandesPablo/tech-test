using ProductComparison.Domain.Exceptions;
using ProductComparison.Domain.ValueObjects;

namespace ProductComparison.Domain.Entities;

public class Product
{
    public int Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public string ImageUrl { get; private set; }
    public Price Price { get; private set; }
    public Rating Rating { get; private set; }
    public ProductSpecifications Specifications { get; private set; }
    public int Version { get; private set; }

    public Product(
        int id,
        string name,
        string description,
        string imageUrl,
        Price price,
        Rating rating,
        ProductSpecifications specifications,
        int version = 0)
    {
        ValidateProduct(name, description, imageUrl);

        Id = id;
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price ?? throw new ArgumentNullException(nameof(price));
        Rating = rating ?? throw new ArgumentNullException(nameof(rating));
        Specifications = specifications ?? throw new ArgumentNullException(nameof(specifications));
        Version = version;
    }

    public void Update(
        string name,
        string description,
        string imageUrl,
        Price price,
        Rating rating,
        ProductSpecifications specifications)
    {
        ValidateProduct(name, description, imageUrl);

        Name = name;
        Description = description;
        ImageUrl = imageUrl;
        Price = price ?? throw new ArgumentNullException(nameof(price));
        Rating = rating ?? throw new ArgumentNullException(nameof(rating));
        Specifications = specifications ?? throw new ArgumentNullException(nameof(specifications));
    }

    public void IncrementVersion()
    {
        Version++;
    }

    private static void ValidateProduct(string name, string description, string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ProductValidationException("Name cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ProductValidationException("Description cannot be empty");
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ProductValidationException("Image URL cannot be empty");
        }

        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            throw new ProductValidationException("Invalid image URL");
        }
    }
}
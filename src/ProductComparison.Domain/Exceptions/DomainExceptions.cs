namespace ProductComparison.Domain.Exceptions;

public abstract class DomainException : Exception
{
    public int StatusCode { get; }

    protected DomainException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

public class ProductNotFoundException : DomainException
{
    public ProductNotFoundException(int id)
        : base($"Product with ID {id} was not found.", 404)
    {
    }
}

public class ProductValidationException : DomainException
{
    public ProductValidationException(string message)
        : base(message, 400)
    {
    }
}

public class ProductComparisonException : DomainException
{
    public ProductComparisonException(string message)
        : base(message, 400)
    {
    }
}

public class ConcurrencyException : DomainException
{
    public ConcurrencyException(string message)
        : base(message, 409)
    {
    }
}
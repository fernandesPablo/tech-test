namespace ProductComparison.CrossCutting.Middleware;

public record ErrorResponse(int StatusCode, string Message, string? Details = null);
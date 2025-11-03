using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductComparison.Domain.Exceptions;

namespace ProductComparison.CrossCutting.Middleware;

public class ExceptionHandlingMiddleware : IMiddleware
{
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Middleware captured an unhandled exception!");
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        var errorResponse = exception switch
        {
            ProductNotFoundException ex => new ErrorResponse(
                404,
                ex.Message,
                null),

            ConcurrencyException ex => new ErrorResponse(
                409,
                ex.Message,
                "The resource was modified by another request. Please refresh and try again."),

            DataFileNotFoundException ex => new ErrorResponse(
                500,
                ex.Message,
                _env.IsDevelopment() ? $"File: {ex.FileName}, Path: {ex.FilePath}" : null),

            DomainException ex => new ErrorResponse(
                ex.StatusCode,
                ex.Message),

            _ => new ErrorResponse(
                500,
                "An internal server error occurred.",
                $"Please contact support with the trace ID: {traceId}.")
        };

        context.Response.StatusCode = errorResponse.StatusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(errorResponse);
    }
}
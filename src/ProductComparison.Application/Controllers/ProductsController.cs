using Microsoft.AspNetCore.Mvc;
using ProductComparison.Domain.Interfaces;
using ProductComparison.Domain.DTOs;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace ProductComparison.Application.Controllers;

/// <summary>
/// Controller for managing product operations and comparisons
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>
    /// Gets the correlation ID from the current activity or HTTP context.
    /// </summary>
    private string GetCorrelationId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// Executes an action within a logging scope with correlation tracking.
    /// </summary>
    private async Task<T> ExecuteWithLoggingAsync<T>(
        string endpoint,
        string action,
        Func<string, Task<T>> operation,
        Dictionary<string, object>? additionalScope = null)
    {
        var correlationId = GetCorrelationId();
        var scope = new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["Endpoint"] = endpoint,
            ["Action"] = action,
            ["Timestamp"] = DateTime.UtcNow
        };

        if (additionalScope != null)
        {
            foreach (var kvp in additionalScope)
            {
                scope[kvp.Key] = kvp.Value;
            }
        }

        using (_logger.BeginScope(scope))
        {
            return await operation(correlationId);
        }
    }

    /// <summary>
    /// Validates the model state and returns BadRequest if invalid.
    /// </summary>
    private ActionResult<T>? ValidateModelState<T>(string correlationId)
    {
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Request failed due to validation errors. CorrelationId: {CorrelationId}", correlationId);
            return BadRequest(ModelState);
        }
        return null;
    }

    /// <summary>
    /// Retrieves all products from the catalog with pagination
    /// </summary>
    /// <param name="page">Page number (default: 1, min: 1)</param>
    /// <param name="pageSize">Number of items per page (default: 10, min: 1, max: 100)</param>
    /// <returns>A paginated list of products</returns>
    /// <response code="200">Returns the paginated list of products</response>
    /// <response code="400">If page or pageSize parameters are invalid</response>
    /// <response code="500">If an internal server error occurs</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<object>> GetAll(
        [FromQuery][Range(1, int.MaxValue, ErrorMessage = "Page must be greater than 0")] int page = 1,
        [FromQuery][Range(1, 100, ErrorMessage = "Page size must be between 1 and 100")] int pageSize = 10)
    {
        return await ExecuteWithLoggingAsync(
            "GET /api/v1/products",
            nameof(GetAll),
            async correlationId =>
            {
                _logger.LogInformation("Received request to get products page {Page} with size {PageSize}. CorrelationId: {CorrelationId}", page, pageSize, correlationId);

                var response = await _productService.GetAllAsync(page, pageSize);

                _logger.LogInformation("Returning page {Page} with {ItemCount} of {TotalCount} products. CorrelationId: {CorrelationId}", page, response.Items.Count(), response.TotalCount, correlationId);

                return Ok(new
                {
                    data = response.Items,
                    pagination = new
                    {
                        page = response.Page,
                        pageSize = response.PageSize,
                        totalCount = response.TotalCount,
                        totalPages = response.TotalPages,
                        hasPreviousPage = response.HasPreviousPage,
                        hasNextPage = response.HasNextPage
                    }
                });
            },
            new Dictionary<string, object>
            {
                ["Page"] = page,
                ["PageSize"] = pageSize
            });
    }

    /// <summary>
    /// Retrieves a specific product by its ID
    /// </summary>
    /// <param name="id">The unique identifier of the product (GUID)</param>
    /// <returns>The product details</returns>
    /// <response code="200">Returns the requested product</response>
    /// <response code="400">If the ID is invalid</response>
    /// <response code="404">If the product is not found</response>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponseDto>> GetById(Guid id)
    {
        return await ExecuteWithLoggingAsync(
            $"GET /api/v1/products/{id}",
            nameof(GetById),
            async correlationId =>
            {
                _logger.LogInformation("Received request to get product by ID: {ProductId}. CorrelationId: {CorrelationId}", id, correlationId);

                var response = await _productService.GetByIdAsync(id);

                _logger.LogInformation("Successfully retrieved product {ProductId}. CorrelationId: {CorrelationId}", id, correlationId);

                return Ok(response);
            },
            new Dictionary<string, object>
            {
                ["ProductId"] = id
            });
    }

    /// <summary>
    /// Compares multiple products and provides price analysis
    /// </summary>
    /// <param name="ids">Comma-separated list of product IDs to compare (e.g., "1,2,3")</param>
    /// <returns>Comparison result including products and price differences</returns>
    /// <response code="200">Returns the comparison result with price analysis</response>
    /// <response code="400">If the IDs format is invalid or empty</response>
    /// <response code="404">If any of the specified products is not found</response>
    /// <example>GET /api/v1/products/compare?ids=1,2,3</example>
    [HttpGet("compare")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductComparisonDto>> Compare(
        [FromQuery]
        [Required(ErrorMessage = "Product IDs are required")]
        [RegularExpression(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(,[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})*$", ErrorMessage = "Product IDs must be comma-separated GUIDs")]
        string ids)
    {
        return await ExecuteWithLoggingAsync(
            "GET /api/v1/products/compare",
            nameof(Compare),
            async correlationId =>
            {
                _logger.LogInformation("Received request to compare products with IDs: {ProductIds}. CorrelationId: {CorrelationId}", ids, correlationId);

                var response = await _productService.CompareAsync(ids);

                _logger.LogInformation("Successfully completed comparison for {ProductCount} products. CorrelationId: {CorrelationId}", response.Products.Count, correlationId);

                return Ok(response);
            },
            new Dictionary<string, object>
            {
                ["RequestedIds"] = ids
            });
    }

    /// <summary>
    /// Creates a new product in the catalog
    /// </summary>
    /// <param name="request">The product data to create</param>
    /// <returns>The created product with its assigned ID</returns>
    /// <response code="201">Returns the newly created product</response>
    /// <response code="400">If the product data is invalid or validation fails</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponseDto>> Create([FromBody] CreateProductDto request)
    {
        return await ExecuteWithLoggingAsync(
            "POST /api/v1/products",
            nameof(Create),
            async correlationId =>
            {
                _logger.LogInformation("Received request to create product: {ProductName} with price {Price}. CorrelationId: {CorrelationId}",
                    request.Name, request.Price, correlationId);

                var validationResult = ValidateModelState<ProductResponseDto>(correlationId);
                if (validationResult != null)
                    return validationResult;

                var response = await _productService.CreateAsync(request);

                _logger.LogInformation("Product created successfully with ID {ProductId}. CorrelationId: {CorrelationId}",
                    response.Id, correlationId);

                return CreatedAtAction(
                    nameof(GetById),
                    new { id = response.Id },
                    response);
            },
            new Dictionary<string, object>
            {
                ["ProductName"] = request.Name,
                ["Price"] = request.Price
            });
    }

    /// <summary>
    /// Updates an existing product in the catalog
    /// </summary>
    /// <param name="id">The unique identifier of the product to update (GUID)</param>
    /// <param name="request">The updated product data</param>
    /// <returns>The updated product details</returns>
    /// <response code="200">Returns the updated product</response>
    /// <response code="400">If the ID or product data is invalid</response>
    /// <response code="404">If the product is not found</response>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponseDto>> Update(
        Guid id,
        [FromBody] UpdateProductDto request)
    {
        return await ExecuteWithLoggingAsync(
            $"PUT /api/v1/products/{id}",
            nameof(Update),
            async correlationId =>
            {
                _logger.LogInformation("Received request to update product ID {ProductId}: {ProductName}. CorrelationId: {CorrelationId}",
                    id, request.Name, correlationId);

                var validationResult = ValidateModelState<ProductResponseDto>(correlationId);
                if (validationResult != null)
                    return validationResult;

                var response = await _productService.UpdateAsync(id, request);

                _logger.LogInformation("Product {ProductId} updated successfully. CorrelationId: {CorrelationId}",
                    id, correlationId);

                return Ok(response);
            },
            new Dictionary<string, object>
            {
                ["ProductId"] = id,
                ["ProductName"] = request.Name,
                ["Price"] = request.Price
            });
    }

    /// <summary>
    /// Deletes a product from the catalog
    /// </summary>
    /// <param name="id">The unique identifier of the product to delete (GUID)</param>
    /// <returns>No content on successful deletion</returns>
    /// <response code="204">Product successfully deleted</response>
    /// <response code="400">If the ID is invalid</response>
    /// <response code="404">If the product is not found</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Delete(Guid id)
    {
        return await ExecuteWithLoggingAsync(
            $"DELETE /api/v1/products/{id}",
            nameof(Delete),
            async correlationId =>
            {
                _logger.LogInformation("Received request to delete product ID {ProductId}. CorrelationId: {CorrelationId}",
                    id, correlationId);

                await _productService.DeleteAsync(id);

                _logger.LogInformation("Product {ProductId} deleted successfully. CorrelationId: {CorrelationId}",
                    id, correlationId);

                return NoContent();
            },
            new Dictionary<string, object>
            {
                ["ProductId"] = id
            });
    }
}
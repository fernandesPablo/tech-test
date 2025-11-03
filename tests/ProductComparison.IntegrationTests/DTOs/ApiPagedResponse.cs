using ProductComparison.Domain.DTOs;

namespace ProductComparison.IntegrationTests.DTOs;

/// <summary>
/// Representa a resposta paginada da API conforme retornado pelo controller.
/// Estrutura: { "data": [...], "pagination": {...} }
/// </summary>
public class ApiPagedResponse<T>
{
    public List<T> Data { get; set; } = new();
    public PaginationInfo Pagination { get; set; } = new();
}

public class PaginationInfo
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}

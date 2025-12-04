using ProductComparison.Domain.DTOs;
using ProductComparison.Domain.Models;

namespace ProductComparison.Domain.Interfaces
{
    public interface IProductService
    {
        Task<PagedResult<ProductResponseDto>> GetAllAsync(int page = 1, int pageSize = 10);
        Task<ProductResponseDto> GetByIdAsync(Guid id);
        Task<ProductComparisonDto> CompareAsync(string productIds);
        Task<ProductResponseDto> CreateAsync(CreateProductDto createDto);
        Task<ProductResponseDto> UpdateAsync(Guid id, UpdateProductDto updateDto);
        Task DeleteAsync(Guid id);
    }
}
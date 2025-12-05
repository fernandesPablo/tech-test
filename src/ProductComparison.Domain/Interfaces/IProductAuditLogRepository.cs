using ProductComparison.Domain.Entities;

namespace ProductComparison.Domain.Interfaces;

/// <summary>
/// Repository for managing product audit logs.
/// Provides operations to create and retrieve audit history for compliance and debugging.
/// </summary>
public interface IProductAuditLogRepository
{
    /// <summary>
    /// Creates a new audit log entry for a product operation.
    /// </summary>
    Task<ProductAuditLog> CreateAsync(ProductAuditLog auditLog);

    /// <summary>
    /// Retrieves all audit log entries for a specific product.
    /// </summary>
    Task<IEnumerable<ProductAuditLog>> GetByProductIdAsync(Guid productId);

    /// <summary>
    /// Retrieves audit logs with pagination.
    /// </summary>
    Task<IEnumerable<ProductAuditLog>> GetPagedAsync(int page = 1, int pageSize = 50);

    /// <summary>
    /// Retrieves audit logs for a specific operation type.
    /// </summary>
    Task<IEnumerable<ProductAuditLog>> GetByOperationTypeAsync(AuditOperationType operationType);
}

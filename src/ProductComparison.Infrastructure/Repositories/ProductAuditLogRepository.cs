using ProductComparison.Domain.Entities;
using ProductComparison.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ProductComparison.Infrastructure.Repositories;

/// <summary>
/// In-memory implementation of ProductAuditLogRepository for CSV-based storage.
/// In a production system with a relational database, this would use EF Core or direct SQL.
/// </summary>
public class ProductAuditLogRepository : IProductAuditLogRepository
{
    private readonly List<ProductAuditLog> _auditLogs = new();
    private readonly ILogger<ProductAuditLogRepository> _logger;
    private static readonly object _lock = new();

    public ProductAuditLogRepository(ILogger<ProductAuditLogRepository> logger)
    {
        _logger = logger;
    }

    public async Task<ProductAuditLog> CreateAsync(ProductAuditLog auditLog)
    {
        ArgumentNullException.ThrowIfNull(auditLog);

        lock (_lock)
        {
            _auditLogs.Add(auditLog);
            _logger.LogInformation(
                "Audit log created: Product {ProductId}, Operation: {Operation}, Version: {Version}, ChangedBy: {ChangedBy}",
                auditLog.ProductId, auditLog.OperationType, auditLog.NewVersion, auditLog.ChangedBy);
        }

        return await Task.FromResult(auditLog);
    }

    public async Task<IEnumerable<ProductAuditLog>> GetByProductIdAsync(Guid productId)
    {
        IEnumerable<ProductAuditLog> logs;

        lock (_lock)
        {
            logs = _auditLogs
                .Where(log => log.ProductId == productId)
                .OrderByDescending(log => log.Timestamp)
                .ToList();

            _logger.LogDebug("Retrieved {Count} audit logs for product {ProductId}", logs.Count(), productId);
        }

        return await Task.FromResult(logs);
    }

    public async Task<IEnumerable<ProductAuditLog>> GetPagedAsync(int page = 1, int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 50;

        IEnumerable<ProductAuditLog> logs;

        lock (_lock)
        {
            logs = _auditLogs
                .OrderByDescending(log => log.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        return await Task.FromResult(logs);
    }

    public async Task<IEnumerable<ProductAuditLog>> GetByOperationTypeAsync(AuditOperationType operationType)
    {
        IEnumerable<ProductAuditLog> logs;

        lock (_lock)
        {
            logs = _auditLogs
                .Where(log => log.OperationType == operationType)
                .OrderByDescending(log => log.Timestamp)
                .ToList();

            _logger.LogDebug("Retrieved {Count} audit logs for operation type {OperationType}", logs.Count(), operationType);
        }

        return await Task.FromResult(logs);
    }
}

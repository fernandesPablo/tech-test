namespace ProductComparison.Domain.Entities;

/// <summary>
/// Represents an audit log entry for product changes.
/// Used for compliance, debugging, and tracking product modifications over time.
/// </summary>
public class ProductAuditLog
{
    /// <summary>
    /// Unique identifier for this audit log entry
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The ID of the product that was modified
    /// </summary>
    public Guid ProductId { get; private set; }

    /// <summary>
    /// The type of operation (Create, Update, Delete)
    /// </summary>
    public AuditOperationType OperationType { get; private set; }

    /// <summary>
    /// The timestamp when the operation occurred (UTC)
    /// </summary>
    public DateTime Timestamp { get; private set; }

    /// <summary>
    /// Previous version number (for updates)
    /// </summary>
    public int? PreviousVersion { get; private set; }

    /// <summary>
    /// New version number after the operation
    /// </summary>
    public int NewVersion { get; private set; }

    /// <summary>
    /// JSON serialization of the previous state (null for creates)
    /// </summary>
    public string? PreviousState { get; private set; }

    /// <summary>
    /// JSON serialization of the new state
    /// </summary>
    public string NewState { get; private set; } = null!;

    /// <summary>
    /// Summary of what changed (e.g., "Name: 'Old' -> 'New', Price: 100 -> 150")
    /// </summary>
    public string ChangeSummary { get; private set; } = null!;

    /// <summary>
    /// User or system that made the change (for future multi-user support)
    /// </summary>
    public string ChangedBy { get; private set; } = "system";

    public ProductAuditLog(
        Guid productId,
        AuditOperationType operationType,
        int newVersion,
        string newState,
        string changeSummary,
        int? previousVersion = null,
        string? previousState = null,
        string? changedBy = null)
    {
        Id = Guid.NewGuid();
        ProductId = productId;
        OperationType = operationType;
        Timestamp = DateTime.UtcNow;
        PreviousVersion = previousVersion;
        NewVersion = newVersion;
        PreviousState = previousState;
        NewState = newState;
        ChangeSummary = changeSummary;
        ChangedBy = changedBy ?? "system";
    }
}

/// <summary>
/// Enumeration for types of operations tracked in audit logs
/// </summary>
public enum AuditOperationType
{
    Create = 1,
    Update = 2,
    Delete = 3
}

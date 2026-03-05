// ProductComparison.Infrastructure/Configuration/RateLimitingOptions.cs
using System.ComponentModel.DataAnnotations;

namespace ProductComparison.Infrastructure.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10000, ErrorMessage = "PermitLimit must be between 1 and 10000")]
    public int PermitLimit { get; set; } = 100;

    [Range(1, 1440, ErrorMessage = "WindowMinutes must be between 1 and 1440 (24 hours)")]
    public int WindowMinutes { get; set; } = 1;

    [Range(0, 1000, ErrorMessage = "QueueLimit must be between 0 and 1000")]
    public int QueueLimit { get; set; } = 10;

    [Range(1, 3600, ErrorMessage = "RetryAfterSeconds must be between 1 and 3600 (1 hour)")]
    public int RetryAfterSeconds { get; set; } = 60;
}
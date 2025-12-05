using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ProductComparison.Infrastructure.HealthChecks;

/// <summary>
/// Health check for Redis cache connectivity and performance.
/// Verifies that Redis is accessible and responsive to PING commands.
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redisConnection;
    private readonly ILogger<RedisHealthCheck>? _logger;

    public RedisHealthCheck(IConnectionMultiplexer redisConnection, ILogger<RedisHealthCheck>? logger = null)
    {
        _redisConnection = redisConnection ?? throw new ArgumentNullException(nameof(redisConnection));
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Starting Redis health check");

            // Check if connection is established
            if (!_redisConnection.IsConnected)
            {
                _logger?.LogWarning("Redis connection is not established");
                return HealthCheckResult.Unhealthy("Redis connection is not established");
            }

            // Get the first server endpoint
            var endpoints = _redisConnection.GetEndPoints();
            if (endpoints.Length == 0)
            {
                _logger?.LogWarning("No Redis endpoints available");
                return HealthCheckResult.Unhealthy("No Redis endpoints available");
            }

            var server = _redisConnection.GetServer(endpoints[0]);

            // Send PING command to verify responsiveness
            var pingResult = await server.PingAsync();
            var duration = pingResult.TotalMilliseconds;

            _logger?.LogDebug("Redis PING response: {Duration}ms", duration);

            // Build detailed response message
            var message = $"Redis cache is operational (PING: {duration:F2}ms)";

            if (duration > 100)
            {
                _logger?.LogWarning("Redis health check: Slow response time {Duration}ms", duration);
                return HealthCheckResult.Degraded(message);
            }

            _logger?.LogDebug("Redis health check completed successfully");
            return HealthCheckResult.Healthy(message);
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Redis health check timeout");
            return HealthCheckResult.Unhealthy("Redis health check timeout", ex);
        }
        catch (RedisConnectionException ex)
        {
            _logger?.LogError(ex, "Redis connection error");
            return HealthCheckResult.Unhealthy("Redis connection error", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis health check failed", ex);
        }
    }
}

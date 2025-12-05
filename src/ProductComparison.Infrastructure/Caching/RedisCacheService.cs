using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProductComparison.Domain.Interfaces;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductComparison.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _distributedCache;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly IConnectionMultiplexer? _redisConnection;
    private readonly string _instancePrefix;
    private static readonly TimeSpan DefaultSlidingExpiration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultAbsoluteExpiration = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RedisCacheService(
        IDistributedCache distributedCache,
        ILogger<RedisCacheService> logger,
        IOptions<RedisCacheOptions>? redisOptions = null,
        IConnectionMultiplexer? redisConnection = null)
    {
        _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redisConnection = redisConnection;
        _instancePrefix = redisOptions?.Value.InstanceName ?? "ProdComparison:";
    }

    /// <summary>
    /// Executes a cache operation with granular exception handling.
    /// Distinguishes between cache misses, serialization errors, and infrastructure failures.
    /// </summary>
    private async Task<T?> ExecuteSafelyAsync<T>(
        Func<Task<T?>> operation,
        string operationName,
        string key)
        where T : class
    {
        try
        {
            return await operation();
        }
        catch (JsonException ex)
        {
            // Serialization error - log as warning and return null to trigger fresh fetch
            _logger.LogWarning(ex, "Failed to deserialize cached value for {Operation} with key: {CacheKey}. Returning null to fetch fresh data.", operationName, key);
            return default;
        }
        catch (OperationCanceledException ex)
        {
            // Redis timeout or cancellation - log and return null to fetch fresh data
            _logger.LogWarning(ex, "Cache operation timeout/cancelled for {Operation} with key: {CacheKey}. Returning null to fetch fresh data.", operationName, key);
            return default;
        }
        catch (Exception ex)
        {
            // Infrastructure/connection error - log as error but still return null to allow service to continue
            _logger.LogError(ex, "Cache infrastructure error during {Operation} for key: {CacheKey}. Cache is unavailable. Returning null to fetch fresh data.", operationName, key);
            return default;
        }
    }

    /// <summary>
    /// Executes a cache operation returning a non-nullable value with graceful degradation on error.
    /// </summary>
    private async Task<T> ExecuteSafelyAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        string key,
        T defaultValue)
    {
        try
        {
            return await operation();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached value for {Operation} with key: {CacheKey}. Returning default value.", operationName, key);
            return defaultValue;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Cache operation timeout/cancelled for {Operation} with key: {CacheKey}. Returning default value.", operationName, key);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache infrastructure error during {Operation} for key: {CacheKey}. Cache is unavailable. Returning default value.", operationName, key);
            return defaultValue;
        }
    }

    /// <summary>
    /// Executes a void cache operation with granular exception handling.
    /// Non-critical operations like Set should not crash on cache failures.
    /// </summary>
    private async Task ExecuteSafelyAsync(
        Func<Task> operation,
        string operationName,
        string key)
    {
        try
        {
            await operation();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to serialize for {Operation} with key: {CacheKey}. Skipping cache write.", operationName, key);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Cache operation timeout/cancelled for {Operation} with key: {CacheKey}. Skipping cache write.", operationName, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache infrastructure error during {Operation} for key: {CacheKey}. Cache is unavailable. Skipping cache write.", operationName, key);
        }
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        return await ExecuteSafelyAsync(async () =>
        {
            var cachedValue = await _distributedCache.GetStringAsync(key);

            if (string.IsNullOrEmpty(cachedValue))
            {
                _logger.LogDebug("Cache miss for key: {CacheKey}", key);
                return default(T);
            }

            var result = JsonSerializer.Deserialize<T>(cachedValue, _jsonOptions);
            _logger.LogDebug("Cache hit for key: {CacheKey}", key);
            return result;
        }, "GetAsync", key, default(T)!);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (value == null)
        {
            _logger.LogDebug("Skipping cache set for key {CacheKey} - value is null", key);
            return;
        }

        await ExecuteSafelyAsync(async () =>
        {
            var jsonValue = JsonSerializer.Serialize(value, _jsonOptions);

            var options = new DistributedCacheEntryOptions();

            if (expiration.HasValue)
            {
                // Quando expiration é fornecida, usa como absoluta
                options.SetAbsoluteExpiration(expiration.Value);
            }
            else
            {
                // Padrão: combina sliding (renovável) + absolute (limite máximo)
                options.SetSlidingExpiration(DefaultSlidingExpiration);
                options.SetAbsoluteExpiration(DefaultAbsoluteExpiration);
            }

            await _distributedCache.SetStringAsync(key, jsonValue, options);
            _logger.LogDebug("Cached value for key: {CacheKey} with expiration: {Expiration}",
                key,
                expiration?.ToString() ?? $"Sliding: {DefaultSlidingExpiration}, Absolute: {DefaultAbsoluteExpiration}");
        }, "SetAsync", key);
    }

    public async Task RemoveAsync(string key)
    {
        await ExecuteSafelyAsync(async () =>
        {
            await _distributedCache.RemoveAsync(key);
            _logger.LogDebug("Removed cache for key: {CacheKey}", key);
        }, "RemoveAsync", key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        return await ExecuteSafelyAsync(async () =>
        {
            var cachedValue = await _distributedCache.GetAsync(key);
            var exists = cachedValue != null;
            _logger.LogDebug("Cache existence check for key {CacheKey}: {Exists}", key, exists);
            return exists;
        }, "ExistsAsync", key, false);
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        await ExecuteSafelyAsync(async () =>
        {
            if (_redisConnection == null)
            {
                _logger.LogWarning("ConnectionMultiplexer not available for pattern removal: {Pattern}", pattern);
                return;
            }

            var fullPattern = $"{_instancePrefix}{pattern}";
            var server = _redisConnection.GetServer(_redisConnection.GetEndPoints()[0]);
            var database = _redisConnection.GetDatabase();
            var keysToDelete = new List<RedisKey>();

            await foreach (var key in server.KeysAsync(pattern: fullPattern))
            {
                keysToDelete.Add(key);
            }

            if (keysToDelete.Count > 0)
            {
                await database.KeyDeleteAsync(keysToDelete.ToArray());
                _logger.LogInformation("Invalidated {Count} cache keys matching pattern: {Pattern}", keysToDelete.Count, fullPattern);
            }
        }, "RemoveByPatternAsync", pattern);
    }
}
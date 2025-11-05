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

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var cachedValue = await _distributedCache.GetStringAsync(key);

            if (string.IsNullOrEmpty(cachedValue))
            {
                _logger.LogDebug("Cache miss for key: {CacheKey}", key);
                return default;
            }

            var result = JsonSerializer.Deserialize<T>(cachedValue, _jsonOptions);
            _logger.LogDebug("Cache hit for key: {CacheKey}", key);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached value for key: {CacheKey}", key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache for key: {CacheKey}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (value == null)
        {
            _logger.LogDebug("Skipping cache set for key {CacheKey} - value is null", key);
            return;
        }

        try
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
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to serialize value for cache key: {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache for key: {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            await _distributedCache.RemoveAsync(key);
            _logger.LogDebug("Removed cache for key: {CacheKey}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache for key: {CacheKey}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var cachedValue = await _distributedCache.GetAsync(key);
            var exists = cachedValue != null;
            _logger.LogDebug("Cache existence check for key {CacheKey}: {Exists}", key, exists);
            return exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking cache existence for key: {CacheKey}", key);
            return false;
        }
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        if (_redisConnection == null)
        {
            _logger.LogWarning("ConnectionMultiplexer not available for pattern removal: {Pattern}", pattern);
            return;
        }

        try
        {
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cache by pattern: {Pattern}", pattern);
        }
    }
}
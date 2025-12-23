using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Rolling.Infrastructure.Persistence.Memory;

public interface IInMemoryCacheService
{
    T? Get<T>(string key);
    void Set<T>(string key, T data, TimeSpan? ttl = null);
    void Delete(string key);
    int DeleteByPattern(string pattern);
    bool Exists(string key);
    TimeSpan? GetTTL(string key);
    int Clear();
    CacheStats GetStats();
}

public class CacheStats
{
    public int TotalKeys { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int DefaultTTL { get; set; }
    public List<string> Keys { get; set; } = new();
}

public class InMemoryCacheService : IInMemoryCacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ILogger<InMemoryCacheService> _logger;
    private readonly string _keyPrefix;
    private readonly int _defaultTtl;

    public InMemoryCacheService(
        ILogger<InMemoryCacheService> logger,
        string keyPrefix = "rolling:",
        int defaultTtl = 3600)
    {
        _logger = logger;
        _keyPrefix = keyPrefix;
        _defaultTtl = defaultTtl;
    }

    private string GetKey(string key) => $"{_keyPrefix}{key}";

    public T? Get<T>(string key)
    {
        try
        {
            var cacheKey = GetKey(key);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.IsExpired())
                {
                    _cache.TryRemove(cacheKey, out _);
                    _logger.LogInformation("🔍 Cache MISS (expired): {Key}", key);
                    return default;
                }

                _logger.LogInformation("✅ Cache HIT: {Key}", key);
                return (T?)entry.Data;
            }

            _logger.LogInformation("🔍 Cache MISS: {Key}", key);
            return default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache GET error for key \"{Key}\"", key);
            return default;
        }
    }

    public void Set<T>(string key, T data, TimeSpan? ttl = null)
    {
        try
        {
            var cacheKey = GetKey(key);
            var expiry = ttl ?? TimeSpan.FromSeconds(_defaultTtl);
            var expiresAt = DateTime.UtcNow.Add(expiry);

            var entry = new CacheEntry
            {
                Data = data,
                ExpiresAt = expiresAt
            };

            _cache[cacheKey] = entry;
            _logger.LogInformation("💾 Cached: {Key} (TTL: {TTL}s)", key, expiry.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache SET error for key \"{Key}\"", key);
        }
    }

    public void Delete(string key)
    {
        try
        {
            var cacheKey = GetKey(key);
            _cache.TryRemove(cacheKey, out _);
            _logger.LogInformation("🗑️  Deleted cache: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache DELETE error for key \"{Key}\"", key);
        }
    }

    public int DeleteByPattern(string pattern)
    {
        try
        {
            var searchPattern = GetKey(pattern);
            var keysToDelete = _cache.Keys
                .Where(k => IsPatternMatch(k, searchPattern))
                .ToList();

            if (keysToDelete.Count == 0)
            {
                _logger.LogInformation("No keys found for pattern: {Pattern}", pattern);
                return 0;
            }

            foreach (var key in keysToDelete)
            {
                _cache.TryRemove(key, out _);
            }

            _logger.LogInformation("🗑️  Deleted {Count} keys matching: {Pattern}", keysToDelete.Count, pattern);
            return keysToDelete.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache DELETE BY PATTERN error for \"{Pattern}\"", pattern);
            return 0;
        }
    }

    public bool Exists(string key)
    {
        try
        {
            var cacheKey = GetKey(key);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.IsExpired())
                {
                    _cache.TryRemove(cacheKey, out _);
                    return false;
                }
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache EXISTS error for key \"{Key}\"", key);
            return false;
        }
    }

    public TimeSpan? GetTTL(string key)
    {
        try
        {
            var cacheKey = GetKey(key);
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                if (entry.IsExpired())
                {
                    _cache.TryRemove(cacheKey, out _);
                    return null;
                }
                return entry.ExpiresAt - DateTime.UtcNow;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache TTL error for key \"{Key}\"", key);
            return null;
        }
    }

    public int Clear()
    {
        try
        {
            var pattern = GetKey("*");
            var keysToDelete = _cache.Keys
                .Where(k => IsPatternMatch(k, pattern))
                .ToList();

            if (keysToDelete.Count == 0)
            {
                _logger.LogInformation("No cache keys to clear");
                return 0;
            }

            foreach (var key in keysToDelete)
            {
                _cache.TryRemove(key, out _);
            }

            _logger.LogInformation("🗑️  Cleared all cache ({Count} keys)", keysToDelete.Count);
            return keysToDelete.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache CLEAR error");
            return 0;
        }
    }

    public CacheStats GetStats()
    {
        try
        {
            var pattern = GetKey("*");
            var keys = _cache.Keys
                .Where(k => IsPatternMatch(k, pattern))
                .Select(k => k.Replace(_keyPrefix, ""))
                .ToList();

            // Clean up expired entries
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.IsExpired())
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            return new CacheStats
            {
                TotalKeys = keys.Count - expiredKeys.Count,
                Prefix = _keyPrefix,
                DefaultTTL = _defaultTtl,
                Keys = keys.Where(k => !expiredKeys.Any(ek => ek.Contains(k))).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache STATS error");
            return new CacheStats
            {
                Prefix = _keyPrefix,
                DefaultTTL = _defaultTtl
            };
        }
    }

    private static bool IsPatternMatch(string input, string pattern)
    {
        // Simple wildcard matching: * matches any characters
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return input.StartsWith(prefix);
        }
        return input == pattern;
    }

    private class CacheEntry
    {
        public object? Data { get; set; }
        public DateTime ExpiresAt { get; set; }

        public bool IsExpired() => DateTime.UtcNow >= ExpiresAt;
    }
}

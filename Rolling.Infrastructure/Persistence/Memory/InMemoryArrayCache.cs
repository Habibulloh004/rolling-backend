using System.Collections.Concurrent;
using System.Linq;

namespace Rolling.Infrastructure.Persistence.Memory;

public sealed record ArrayCacheEntry<T>(T Data, DateTimeOffset UpdatedAt, long Version);

public sealed class KeyedInMemoryArrayCache<T>
{
    private readonly ConcurrentDictionary<string, ArrayCacheEntry<T>> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private long _versionCounter;

    public KeyedInMemoryArrayCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public ArrayCacheEntry<T>? Get(string key)
    {
        return _entries.TryGetValue(key, out var entry) ? entry : null;
    }

    public ArrayCacheEntry<T> Set(string key, T data)
    {
        var version = Interlocked.Increment(ref _versionCounter);
        var entry = new ArrayCacheEntry<T>(data, _timeProvider.GetUtcNow(), version);
        _entries[key] = entry;
        return entry;
    }

    public IReadOnlyCollection<string> Keys => _entries.Keys.ToArray();

    public bool Remove(string key) => _entries.TryRemove(key, out _);

    public void Clear() => _entries.Clear();
}

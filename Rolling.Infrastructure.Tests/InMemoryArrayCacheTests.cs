using System.Collections.Concurrent;
using Rolling.Infrastructure.Persistence.Memory;
using Xunit;

namespace Rolling.Infrastructure.Tests;

public sealed class InMemoryArrayCacheTests
{
    [Fact]
    public void Set_IncrementsVersionAndUpdatesTimestamp()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new TestTimeProvider(now);
        var cache = new KeyedInMemoryArrayCache<int>(timeProvider);

        var first = cache.Set("products", 1);
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        var second = cache.Set("products", 2);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.True(second.UpdatedAt > first.UpdatedAt);
        Assert.Equal(2, cache.Get("products")?.Data);
    }

    [Fact]
    public async Task Set_IsThreadSafe_AndVersionsAreUnique()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var cache = new KeyedInMemoryArrayCache<int>(timeProvider);
        var versions = new ConcurrentBag<long>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 64), async (value, _) =>
        {
            var entry = cache.Set("categories", value);
            versions.Add(entry.Version);
            await Task.Yield();
        });

        var distinct = versions.Distinct().ToList();
        Assert.Equal(64, versions.Count);
        Assert.Equal(64, distinct.Count);
        Assert.Equal(64, distinct.Max());
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public TestTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta)
        {
            _now = _now.Add(delta);
        }
    }
}

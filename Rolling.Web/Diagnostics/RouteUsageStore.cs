using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Rolling.Web.Diagnostics;

public sealed class RouteUsageStore
{
    private readonly ConcurrentDictionary<string, RouteUsageCounter> _counters = new(StringComparer.OrdinalIgnoreCase);

    public void Increment(string? routePattern, string requestPath)
    {
        var key = string.IsNullOrWhiteSpace(routePattern) ? "unknown" : routePattern.Trim();
        var path = string.IsNullOrWhiteSpace(requestPath) ? "/" : requestPath;

        _counters.AddOrUpdate(
            key,
            _ => new RouteUsageCounter(key, path, 1),
            (_, counter) =>
            {
                counter.Increment(path);
                return counter;
            });
    }

    public IReadOnlyCollection<RouteUsageSnapshot> GetSnapshot() =>
        _counters.Values
            .Select(counter => new RouteUsageSnapshot(counter.Route, counter.Count, counter.LastPath))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Route, StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class RouteUsageCounter
{
    public string Route { get; }

    private long _count;
    private string _lastPath;

    public RouteUsageCounter(string route, string lastPath, long initialCount)
    {
        Route = route;
        _lastPath = lastPath;
        _count = initialCount;
    }

    public long Count => Interlocked.Read(ref _count);

    public string LastPath => _lastPath;

    public void Increment(string lastPath)
    {
        Interlocked.Increment(ref _count);
        if (!string.IsNullOrWhiteSpace(lastPath))
        {
            _lastPath = lastPath;
        }
    }
}

public sealed record RouteUsageSnapshot(string Route, long Count, string LastPath);

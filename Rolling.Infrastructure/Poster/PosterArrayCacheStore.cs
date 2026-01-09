using System.Text.Json;
using Rolling.Infrastructure.Persistence.Memory;

namespace Rolling.Infrastructure.Poster;

public sealed class PosterArrayCacheStore
{
    public const string DefaultKey = "default";

    public PosterArrayCacheStore(TimeProvider timeProvider)
    {
        Products = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
        Categories = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
        Promotions = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
        ClientGroups = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
        Spots = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
        Employees = new KeyedInMemoryArrayCache<JsonDocument?>(timeProvider);
    }

    public KeyedInMemoryArrayCache<JsonDocument?> Products { get; }
    public KeyedInMemoryArrayCache<JsonDocument?> Categories { get; }
    public KeyedInMemoryArrayCache<JsonDocument?> Promotions { get; }
    public KeyedInMemoryArrayCache<JsonDocument?> ClientGroups { get; }
    public KeyedInMemoryArrayCache<JsonDocument?> Spots { get; }
    public KeyedInMemoryArrayCache<JsonDocument?> Employees { get; }
}

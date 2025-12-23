using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Rolling.Infrastructure.Notifications;

public sealed class NotificationTokenStore
{
    private readonly ConcurrentDictionary<string, NotificationTokenEntry> _tokens = new();

    public IReadOnlyCollection<KeyValuePair<string, NotificationTokenEntry>> GetAll() => _tokens.ToArray();

    public bool TryGet(string token, [MaybeNullWhen(false)] out NotificationTokenEntry entry) => _tokens.TryGetValue(token, out entry);

    public void AddOrUpdate(string token, NotificationTokenEntry entry) => _tokens[token] = entry with
    {
        RegisteredAt = entry.RegisteredAt == default ? DateTimeOffset.UtcNow : entry.RegisteredAt,
        LastUsed = DateTimeOffset.UtcNow
    };

    public bool Remove(string token) => _tokens.TryRemove(token, out _);

    public int Count => _tokens.Count;

    public NotificationStats GetStats()
    {
        var stats = new NotificationStats();
        var today = DateTimeOffset.UtcNow.Date;

        foreach (var (_, entry) in _tokens)
        {
            if (entry.IsValid == false)
            {
                stats.InvalidTokens++;
            }
            else
            {
                stats.ValidTokens++;
            }

            if (!string.IsNullOrWhiteSpace(entry.Language))
            {
                var key = entry.Language!;
                if (stats.LanguageBreakdown.TryGetValue(key, out var count))
                {
                    stats.LanguageBreakdown[key] = count + 1;
                }
                else
                {
                    stats.LanguageBreakdown[key] = 1;
                }
            }

            if (entry.RegisteredAt.Date == today)
            {
                stats.RegisteredToday++;
            }
        }

        stats.TotalTokens = stats.ValidTokens + stats.InvalidTokens;
        return stats;
    }

    public void Cleanup(TimeSpan maxInactivity)
    {
        foreach (var (token, entry) in _tokens)
        {
            if (entry.IsValid == false || DateTimeOffset.UtcNow - entry.LastUsed > maxInactivity)
            {
                _tokens.TryRemove(token, out _);
            }
        }
    }
}

public sealed record NotificationTokenEntry(
    string? Language,
    string? UserId,
    DateTimeOffset RegisteredAt,
    DateTimeOffset LastUsed,
    bool? IsValid);

public sealed class NotificationStats
{
    public int TotalTokens { get; set; }
    public int ValidTokens { get; set; }
    public int InvalidTokens { get; set; }
    public int RegisteredToday { get; set; }
    public Dictionary<string, int> LanguageBreakdown { get; } = new(StringComparer.OrdinalIgnoreCase);
}

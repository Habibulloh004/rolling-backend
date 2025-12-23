namespace Rolling.Infrastructure.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "localhost:6379";

    public string NotificationsKey { get; set; } = "rolling:notifications";

    public int HistorySize { get; set; } = 100;

    public string ChatThreadKeyPrefix { get; set; } = "rolling:chat:threads";

    public int ChatRecentMessageLimit { get; set; } = 100;
}

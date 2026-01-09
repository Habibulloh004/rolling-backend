using System;

namespace Rolling.Infrastructure.Configuration;

public sealed class PosterOptions
{
    public const string SectionName = "Poster";

    public string ApiBaseUrl { get; init; } = GetEnv("POSTER_URL", "https://joinposter.com");

    public string Token { get; init; } = GetEnv("PAST");

    private static string GetEnv(string key, string defaultValue = "")
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}

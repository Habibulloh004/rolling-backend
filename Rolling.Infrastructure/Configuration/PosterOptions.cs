using System;

namespace Rolling.Infrastructure.Configuration;

public sealed class PosterOptions
{
    public const string SectionName = "Poster";

    public string ApiBaseUrl { get; init; } = GetEnv("POSTER_URL", "https://joinposter.com");

    public string Token { get; init; } = GetEnv("PAST");

    public string EmployeeBaseUrl { get; init; } = GetEnv("EMPLOYEE");

    public string SpotsBaseUrl { get; init; } = GetEnv("SPOTS");

    public string SpotsToken { get; init; } = GetEnv("TOKENSUSHI");

    public string OrdersBackendUrl { get; init; } = GetEnv("BACKEND_URL");

    public string AlternateOrdersUrl { get; init; } = GetEnv("AURL");

    private static string GetEnv(string key, string defaultValue = "")
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}

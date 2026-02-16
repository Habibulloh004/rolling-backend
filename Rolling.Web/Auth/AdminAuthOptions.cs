namespace Rolling.Web.Auth;

public sealed class AdminAuthOptions
{
    public const string SectionName = "AdminAuth";

    public string? SigningKey { get; set; }

    public string SeedLogin { get; set; } = "admin";

    public string SeedPassword { get; set; } = "admin12345";

    public int AccessTokenLifetimeMinutes { get; set; } = 15;

    public int RefreshTokenLifetimeDays { get; set; } = 14;
}

namespace Rolling.Infrastructure.Configuration;

public sealed class PlumOptions
{
    public const string SectionName = "Plum";

    public string BaseUrl { get; set; } = "https://pay.myuzcard.uz/api";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Language { get; set; } = "uz";

    public int RequestTimeoutSeconds { get; set; } = 100;
}

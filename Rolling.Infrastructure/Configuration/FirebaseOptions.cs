namespace Rolling.Infrastructure.Configuration;

public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string ProjectId { get; init; } = string.Empty;

    /// <summary>
    /// Absolute or relative path to a service-account JSON file.
    /// </summary>
    public string? CredentialsPath { get; init; }

    /// <summary>
    /// Raw service-account JSON content (used when path access is not available).
    /// </summary>
    public string? CredentialJson { get; init; }

    public bool EnableFirebaseMessaging { get; init; } = true;
}

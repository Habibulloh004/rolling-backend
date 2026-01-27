using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Notifications;

public sealed class NotifyRequest
{
    [JsonPropertyName("fcm")]
    public string Topic { get; set; } = string.Empty;

    [JsonPropertyName("fcm_lng")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("status")]
    public string MessageType { get; set; } = "body";
}

public sealed class TokenRegisterRequest
{
    public string DeviceToken { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string? UserId { get; set; }
}

public sealed class TokenLanguageUpdateRequest
{
    public string DeviceToken { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
}

public sealed class TokenLookupRequest
{
    public string Token { get; set; } = string.Empty;
}

public sealed class TokenValidateRequest
{
    public string DeviceToken { get; set; } = string.Empty;
}

public sealed class SendTopicRequest
{
    public string MessageType { get; set; } = "body";
    public string? CustomTitle { get; set; }
    public string? CustomBody { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

public sealed class SendAllLanguagesRequest
{
    public string MessageType { get; set; } = "body";
    public Dictionary<string, CustomMessagePayload>? CustomMessages { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

public sealed class CustomMessagePayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class SendDeviceRequest
{
    public string DeviceToken { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string MessageType { get; set; } = "body";
    public string? CustomTitle { get; set; }
    public string? CustomBody { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

public sealed class SendDevicesRequest
{
    public List<string> DeviceTokens { get; set; } = new();
    public string Language { get; set; } = "en";
    public string MessageType { get; set; } = "body";
    public string? CustomTitle { get; set; }
    public string? CustomBody { get; set; }
    public Dictionary<string, string>? Data { get; set; }
}

public sealed class SubscribeRequest
{
    public string DeviceToken { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
}

public sealed class UnsubscribeRequest
{
    public string DeviceToken { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
}

public sealed class TestDirectRequest
{
    public string Token { get; set; } = string.Empty;
}

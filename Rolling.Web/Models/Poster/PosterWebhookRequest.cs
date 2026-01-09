using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Poster;

public sealed class PosterWebhookRequest
{
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("object_id")]
    public JsonElement? ObjectId { get; init; }

    public string? ObjectIdValue => ObjectId switch
    {
        null => null,
        _ when ObjectId.Value.ValueKind == JsonValueKind.String => ObjectId.Value.GetString(),
        _ when ObjectId.Value.ValueKind == JsonValueKind.Number => ObjectId.Value.GetRawText(),
        _ => null
    };
}

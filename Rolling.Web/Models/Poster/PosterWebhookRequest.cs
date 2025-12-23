using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Poster;

public sealed class PosterWebhookRequest
{
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("object_id")]
    public string? ObjectId { get; init; }
}

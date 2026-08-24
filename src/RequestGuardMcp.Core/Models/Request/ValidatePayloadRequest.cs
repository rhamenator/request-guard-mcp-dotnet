using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ValidatePayloadRequest</c>.</summary>
public sealed class ValidatePayloadRequest
{
    [JsonRequired]
    public string Tool { get; set; } = "";

    [JsonRequired]
    public JsonNode? Payload { get; set; }
}

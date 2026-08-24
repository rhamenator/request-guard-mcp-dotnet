using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ValidatePayloadRequest</c>.</summary>
public sealed class ValidatePayloadRequest
{
    public string Tool { get; set; } = "";
    public JsonNode? Payload { get; set; }
}

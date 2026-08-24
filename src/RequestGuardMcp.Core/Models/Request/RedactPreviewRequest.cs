using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>RedactPreviewRequest</c>.</summary>
public sealed class RedactPreviewRequest
{
    public JsonNode? Payload { get; set; }
    public List<string>? Fields { get; set; }
}

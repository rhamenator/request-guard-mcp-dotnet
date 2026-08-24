using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ExplainRequest</c>.</summary>
public sealed class ExplainRequest
{
    public JsonNode? Classification { get; set; }
    public string? Format { get; set; }
}

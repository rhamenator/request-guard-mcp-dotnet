using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ExplainRequest</c>.</summary>
public sealed class ExplainRequest
{
    [JsonRequired]
    public JsonNode? Classification { get; set; }
    public string? Format { get; set; }
}

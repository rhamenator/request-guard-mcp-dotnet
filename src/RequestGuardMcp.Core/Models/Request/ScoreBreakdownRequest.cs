using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ScoreBreakdownRequest</c>.</summary>
public sealed class ScoreBreakdownRequest
{
    public string? RequestId { get; set; }
    public JsonNode? Signals { get; set; }
}

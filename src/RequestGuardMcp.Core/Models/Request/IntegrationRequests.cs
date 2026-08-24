using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace RequestGuardMcp.Core.Models.Request;

public sealed record FeedbackRequest([property: JsonRequired] string RequestId, [property: JsonRequired] string CorrectVerdict, string? Notes = null, string? Reporter = null);
public sealed record ReplayRequest([property: JsonRequired] string RequestId, bool? Deterministic = null);
public sealed record EnrichIpRequest([property: JsonRequired] string Ip);
public sealed record EnrichAsnRequest(uint Asn);
public sealed record EnrichUaRequest([property: JsonRequired] string UserAgent);
public sealed record ThreatLookupRequest([property: JsonRequired] string Indicator, [property: JsonPropertyName("type")] string? IndicatorType = null);
public sealed record CanaryEvalRequest([property: JsonRequired] string Token, JsonNode? Context = null);
public sealed record AbusePatternMatchRequest([property: JsonRequired] string Text, IReadOnlyList<string>? Categories = null);
public sealed class DriftReportRequest
{
    public string? Since { get; set; }
    public uint? WindowHours { get; set; }
}

public sealed class CalibrationReportRequest
{
    public uint? WindowHours { get; set; }
}

public sealed class QueueStatusRequest
{
    public string? Queue { get; set; }
}

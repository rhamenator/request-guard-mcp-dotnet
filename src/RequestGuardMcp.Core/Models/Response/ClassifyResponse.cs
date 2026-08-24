using RequestGuardMcp.Core.Models.Enums;

namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Primary classification response. Ports src/models/response.rs's <c>ClassifyResponse</c>.</summary>
public sealed record ClassifyResponse(
    string RequestId,
    Verdict Verdict,
    double Score,
    Confidence Confidence,
    ThreatCategory ThreatCategory,
    IReadOnlyList<SignalHit> Signals,
    ulong LatencyMs,
    string ModelVersion)
{
    /// <summary>Returns a copy with a new request id and latency — used when serving a cached result.</summary>
    public ClassifyResponse WithRequestIdAndLatency(string requestId, ulong latencyMs) =>
        this with { RequestId = requestId, LatencyMs = latencyMs };
}

/// <summary>A single signal that contributed to the classification. Ports src/models/response.rs's <c>SignalHit</c>.</summary>
public sealed record SignalHit(string Name, double Value, double Weight, string Description);

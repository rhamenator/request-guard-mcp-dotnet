using System.Diagnostics;
using System.Text.Json;
using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Core.Classification;

/// <summary>
/// Runs the classify pipeline: rule evaluation, scoring, and the in-process cache. Ports
/// src/tools/classify.rs. PostgreSQL decision persistence and the Redis-backed distributed cache
/// tier are added in phase 4; TLS fingerprint validation/attestation in phase 5.
/// </summary>
public static class ClassifyService
{

    public static Task<ClassifyResponse> RunAsync(AppState state, ClassifyRequest request, string callerScope, CancellationToken cancellationToken = default) =>
        RunInternalAsync(state, request, callerScope, cancellationToken);

    /// <summary>Used by <c>warmup</c> and <c>self_test</c>: identical behavior for now, kept as a
    /// distinct entry point because phase 4 gives it a real difference (skips decision persistence).</summary>
    public static Task<ClassifyResponse> RunEphemeralAsync(AppState state, ClassifyRequest request, CancellationToken cancellationToken = default) =>
        RunInternalAsync(state, request, "internal", cancellationToken);

    private static async Task<ClassifyResponse> RunInternalAsync(AppState state, ClassifyRequest request, string callerScope, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = request.RequestId ?? Guid.NewGuid().ToString();

        var signals = RuleEngine.Evaluate(request);
        var fingerprint = RequestFingerprint.Compute(callerScope, request);

        var cached = await state.Cache.GetAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            var cachedResponse = JsonSerializer.Deserialize<ClassifyResponse>(cached, McpJson.Options);
            if (cachedResponse is not null)
            {
                return cachedResponse.WithRequestIdAndLatency(requestId, (ulong)stopwatch.ElapsedMilliseconds);
            }
        }

        var result = Scorer.Score(signals);
        var signalHits = signals.Signals
            .Select(s => new SignalHit(s.Name, s.Value, s.Weight, s.Description))
            .ToList();

        var response = new ClassifyResponse(
            RequestId: requestId,
            Verdict: result.Verdict,
            Score: result.Score,
            Confidence: result.Confidence,
            ThreatCategory: result.ThreatCategory,
            Signals: signalHits,
            LatencyMs: (ulong)stopwatch.ElapsedMilliseconds,
            ModelVersion: state.BuildInfo.Version);

        var node = JsonSerializer.SerializeToNode(response, McpJson.Options);
        if (node is not null)
        {
            await state.Cache.SetAsync(fingerprint, node, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}

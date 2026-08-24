using System.Diagnostics;
using System.Text.Json;
using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Core.Classification;

/// <summary>
/// Runs the classify pipeline: rule evaluation, scoring, the two-level cache (in-process, then
/// Redis when configured), and PostgreSQL decision persistence when configured. Ports
/// src/tools/classify.rs. Best-effort distributed-cache failures are isolated here so the local
/// cache and the response itself are unaffected either way. TLS metadata participates only
/// after its short-lived, request-bound HMAC attestation is verified.
/// </summary>
public static class ClassifyService
{
    public static Task<ClassifyResponse> RunAsync(AppState state, ClassifyRequest request, string callerScope, CancellationToken cancellationToken = default) =>
        RunInternalAsync(state, request, callerScope, persist: true, cancellationToken);

    /// <summary>Used by <c>warmup</c> and <c>self_test</c>: runs the pipeline without persisting a decision.</summary>
    public static Task<ClassifyResponse> RunEphemeralAsync(AppState state, ClassifyRequest request, CancellationToken cancellationToken = default) =>
        RunInternalAsync(state, request, "internal", persist: false, cancellationToken);

    private static async Task<ClassifyResponse> RunInternalAsync(AppState state, ClassifyRequest request, string callerScope, bool persist, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = request.RequestId ?? Guid.NewGuid().ToString();

        ValidateTlsMetadata(request);
        _ = TlsAttestation.VerifyAndNormalize(request, state.Config.TlsFingerprints, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        request.TlsJa3 = TlsAttestation.NormalizeJa3(request.TlsJa3);
        request.TlsJa4 = TlsAttestation.NormalizeJa4(request.TlsJa4);
        request.TlsFingerprintSource = TlsAttestation.NormalizeSource(request.TlsFingerprintSource);
        request.TlsFingerprintAttestation = null;

        var signals = RuleEngine.Evaluate(request, state.Config.TlsFingerprints);
        var fingerprint = RequestFingerprint.Compute(callerScope, request);

        var distributedCached = await TryGetDistributedCacheAsync(state, fingerprint, cancellationToken).ConfigureAwait(false);
        var cached = distributedCached ?? await state.Cache.GetAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            var cachedResponse = JsonSerializer.Deserialize<ClassifyResponse>(cached, McpJson.Options);
            if (cachedResponse is not null)
            {
                var result = cachedResponse.WithRequestIdAndLatency(requestId, (ulong)stopwatch.ElapsedMilliseconds);
                if (persist)
                {
                    await PersistDecisionAsync(state, request, result, cancellationToken).ConfigureAwait(false);
                }

                return result;
            }
        }

        var scored = Scorer.Score(signals);
        var signalHits = signals.Signals
            .Select(s => new SignalHit(s.Name, s.Value, s.Weight, s.Description))
            .ToList();

        var response = new ClassifyResponse(
            RequestId: requestId,
            Verdict: scored.Verdict,
            Score: scored.Score,
            Confidence: scored.Confidence,
            ThreatCategory: scored.ThreatCategory,
            Signals: signalHits,
            LatencyMs: (ulong)stopwatch.ElapsedMilliseconds,
            ModelVersion: state.BuildInfo.Version);

        var node = JsonSerializer.SerializeToNode(response, McpJson.Options);
        if (node is not null)
        {
            await state.Cache.SetAsync(fingerprint, node, cancellationToken).ConfigureAwait(false);
            if (state.Redis.IsAvailable)
            {
                try
                {
                    await state.Redis.CacheSetAsync(fingerprint, node, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Best-effort: the local cache write above already succeeded.
                }
            }
        }

        if (persist)
        {
            await PersistDecisionAsync(state, request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static async Task<System.Text.Json.Nodes.JsonNode?> TryGetDistributedCacheAsync(AppState state, string fingerprint, CancellationToken cancellationToken)
    {
        if (!state.Redis.IsAvailable)
        {
            return null;
        }

        try
        {
            return await state.Redis.CacheGetAsync(fingerprint, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task PersistDecisionAsync(AppState state, ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken)
    {
        if (!state.Postgres.IsAvailable)
        {
            return;
        }

        try
        {
            await state.Postgres.RecordDecisionAsync(request, response, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw AppErrorException.Upstream($"PostgreSQL decision write failed: {error.Message}");
        }
    }

    private static void ValidateTlsMetadata(ClassifyRequest request)
    {
        if (request.TlsJa3 is not null && TlsAttestation.NormalizeJa3(request.TlsJa3) is null)
        {
            throw AppErrorException.Validation("tls_ja3 must be a 32-character hexadecimal JA3 digest");
        }

        if (request.TlsJa4 is not null && TlsAttestation.NormalizeJa4(request.TlsJa4) is null)
        {
            throw AppErrorException.Validation("tls_ja4 must use the canonical JA4 a_b_c format");
        }

        if (request.TlsFingerprintSource is not null && TlsAttestation.NormalizeSource(request.TlsFingerprintSource) is null)
        {
            throw AppErrorException.Validation("tls_fingerprint_source must be a short infrastructure identifier");
        }
    }
}

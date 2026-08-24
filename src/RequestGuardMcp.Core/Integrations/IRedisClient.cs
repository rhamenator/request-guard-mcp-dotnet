using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>Ports src/integrations/redis.rs's <c>ThreatRecord</c>.</summary>
public sealed record ThreatRecord(string ThreatType, string Severity, string Source, string? LastSeen, JsonNode? Metadata = null);

/// <summary>Ports src/integrations/redis.rs's <c>CanaryRecord</c>.</summary>
public sealed record CanaryRecord(string CanaryId, JsonNode? Metadata = null);

/// <summary>Ports src/integrations/redis.rs's <c>QueueStats</c>.</summary>
public sealed record QueueStats(string Name, ulong Active, ulong CompletedLastMinute);

/// <summary>
/// The Redis-backed distributed cache, threat registry, canary registry, and queue telemetry.
/// Ports src/integrations/redis.rs's <c>RedisClient</c>. When not configured, every read/write
/// method throws <see cref="Errors.AppErrorException.IntegrationUnavailable"/> except
/// <see cref="PingAsync"/>, which returns false — matching the Rust server's disabled-feature
/// stub behavior exactly.
/// </summary>
public interface IRedisClient
{
    public bool IsAvailable { get; }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default);

    public Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default);

    public Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default);

    public Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default);

    public Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default);

    public Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default);

    public Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default);

    public Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default);

    public Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default);

    public Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default);
}

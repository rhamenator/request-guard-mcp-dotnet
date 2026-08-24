using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Errors;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>The disabled-state <see cref="IRedisClient"/>, used until <c>redis.url</c> is configured.</summary>
public sealed class NullRedisClient : IRedisClient
{
    public static readonly NullRedisClient Instance = new();

    public bool IsAvailable => false;

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");

    public Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default) =>
        throw AppErrorException.IntegrationUnavailable("Redis integration is disabled");
}

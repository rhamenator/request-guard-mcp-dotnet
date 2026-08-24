using System.Globalization;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>
/// Redis-backed reputation lookups layered on the threat registry. Ports
/// src/integrations/reputation.rs's <c>ReputationClient</c>. Pure C# with no external
/// dependency of its own — it only needs <see cref="IRedisClient"/> — so unlike the MaxMind/
/// PostgreSQL/Redis adapters it lives in Core rather than the Integrations project.
/// </summary>
public sealed class ReputationClient(IRedisClient redis) : IReputationClient
{
    public bool IsConfigured => redis.IsAvailable;

    public Task<ReputationScore> LookupIpAsync(string ip, CancellationToken cancellationToken = default) =>
        LookupAsync("ip", ip, cancellationToken);

    public Task<ReputationScore> LookupAsnAsync(uint asn, CancellationToken cancellationToken = default) =>
        LookupAsync("asn", asn.ToString(CultureInfo.InvariantCulture), cancellationToken);

    private async Task<ReputationScore> LookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new ReputationScore();
        }

        var record = await redis.ThreatLookupAsync(indicatorType, indicator, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return new ReputationScore();
        }

        var score = record.Severity.ToLowerInvariant() switch
        {
            "critical" => 1.0,
            "high" => 0.9,
            "medium" => 0.6,
            "low" => 0.3,
            _ => 0.5,
        };

        return new ReputationScore(score, Listed: true, Source: record.Source, Categories: [record.ThreatType]);
    }
}

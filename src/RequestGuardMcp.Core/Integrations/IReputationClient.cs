namespace RequestGuardMcp.Core.Integrations;

/// <summary>Ports src/integrations/reputation.rs's <c>ReputationScore</c>.</summary>
public sealed record ReputationScore(double Score = 0.0, bool Listed = false, string? Source = null, IReadOnlyList<string>? Categories = null)
{
    public IReadOnlyList<string> Categories { get; init; } = Categories ?? [];
}

/// <summary>Redis-backed IP/ASN reputation lookups layered on the threat registry. Ports src/integrations/reputation.rs.</summary>
public interface IReputationClient
{
    public bool IsConfigured { get; }

    public Task<ReputationScore> LookupIpAsync(string ip, CancellationToken cancellationToken = default);

    public Task<ReputationScore> LookupAsnAsync(uint asn, CancellationToken cancellationToken = default);
}

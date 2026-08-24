using System.Net;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>Ports src/integrations/geoip.rs's <c>GeoipResult</c>.</summary>
public sealed record GeoipResult(
    string? Country = null,
    string? City = null,
    uint? Asn = null,
    string? Org = null,
    bool IsProxy = false,
    bool IsDatacenter = false,
    bool IsTor = false);

/// <summary>Ports src/integrations/geoip.rs's <c>AsnResult</c>.</summary>
public sealed record AsnResult(string? Organization, bool IsHosting);

/// <summary>
/// MaxMind City/ASN/Anonymous-IP enrichment. Ports src/integrations/geoip.rs's <c>GeoipClient</c>.
/// When no database is configured, <see cref="LookupIp"/> returns an empty result and
/// <see cref="LookupAsn"/> returns null — matching the Rust server's disabled-state behavior,
/// which is "no data" rather than an error (callers decide whether that's acceptable via
/// <see cref="HasIpDatabase"/>/<see cref="HasAsnDatabase"/>).
/// </summary>
public interface IGeoipClient
{
    public bool IsAvailable { get; }

    public bool HasIpDatabase { get; }

    public bool HasAsnDatabase { get; }

    public GeoipResult LookupIp(IPAddress ip);

    public AsnResult? LookupAsn(uint asn);
}

/// <summary>Ports src/integrations/geoip.rs's <c>is_hosting_org</c> heuristic.</summary>
public static class HostingOrganizations
{
    private static readonly string[] Markers =
    [
        "hosting", "cloud", "data center", "datacenter", "digitalocean",
        "amazon", "google", "microsoft", "ovh", "hetzner", "linode", "vultr",
    ];

    public static bool IsHostingOrg(string organization)
    {
        var lower = organization.ToLowerInvariant();
        return Markers.Any(marker => lower.Contains(marker, StringComparison.Ordinal));
    }
}

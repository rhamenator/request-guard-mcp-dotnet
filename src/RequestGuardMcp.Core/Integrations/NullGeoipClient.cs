using System.Net;

namespace RequestGuardMcp.Core.Integrations;

/// <summary>The disabled-state <see cref="IGeoipClient"/>, used until any MaxMind database path is configured.</summary>
public sealed class NullGeoipClient : IGeoipClient
{
    public static readonly NullGeoipClient Instance = new();

    public bool IsAvailable => false;

    public bool HasIpDatabase => false;

    public bool HasAsnDatabase => false;

    public GeoipResult LookupIp(IPAddress ip) => new();

    public AsnResult? LookupAsn(uint asn) => null;
}

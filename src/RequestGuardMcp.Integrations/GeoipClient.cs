using System.Collections.Concurrent;
using System.Net;
using MaxMind.Db;
using MaxMind.GeoIP2;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;

namespace RequestGuardMcp.Integrations;

/// <summary>Read-only MaxMind City, ASN, and Anonymous-IP adapter.</summary>
public sealed class GeoipClient : IGeoipClient, IDisposable
{
    private readonly DatabaseReader? _city;
    private readonly DatabaseReader? _asn;
    private readonly DatabaseReader? _anonymous;
    private readonly ConcurrentDictionary<uint, string> _asnIndex;

    private GeoipClient(DatabaseReader? city, DatabaseReader? asn, DatabaseReader? anonymous, ConcurrentDictionary<uint, string> asnIndex)
    {
        _city = city;
        _asn = asn;
        _anonymous = anonymous;
        _asnIndex = asnIndex;
    }

    public static GeoipClient Load(GeoipConfig config)
    {
        DatabaseReader? city = null;
        DatabaseReader? asn = null;
        DatabaseReader? anonymous = null;
        try
        {
            city = Open(config.CityMmdbPath ?? config.MmdbPath);
            asn = Open(config.AsnMmdbPath);
            anonymous = Open(config.AnonymousIpMmdbPath);
            var index = config.AsnMmdbPath is null ? [] : BuildAsnIndex(config.AsnMmdbPath);
            return new GeoipClient(city, asn, anonymous, index);
        }
        catch
        {
            city?.Dispose();
            asn?.Dispose();
            anonymous?.Dispose();
            throw;
        }
    }

    public bool IsAvailable => _city is not null || _asn is not null || _anonymous is not null;
    public bool HasIpDatabase => IsAvailable;
    public bool HasAsnDatabase => _asn is not null;

    public GeoipResult LookupIp(IPAddress ip)
    {
        string? country = null;
        string? city = null;
        uint? asn = null;
        string? organization = null;
        var isProxy = false;
        var isDatacenter = false;
        var isTor = false;

        if (_city?.TryCity(ip, out var cityResponse) == true)
        {
            country = cityResponse.Country.IsoCode;
            city = cityResponse.City.Name;
        }

        if (_asn?.TryAsn(ip, out var asnResponse) == true)
        {
            asn = asnResponse.AutonomousSystemNumber is >= 0 and <= uint.MaxValue ? (uint)asnResponse.AutonomousSystemNumber.Value : null;
            organization = asnResponse.AutonomousSystemOrganization;
            if (asn is { } number && organization is not null)
            {
                _asnIndex.TryAdd(number, organization);
                isDatacenter = HostingOrganizations.IsHostingOrg(organization);
            }
        }

        if (_anonymous?.TryAnonymousIP(ip, out var anonymousResponse) == true)
        {
            isProxy = anonymousResponse.IsAnonymous || anonymousResponse.IsAnonymousVpn ||
                      anonymousResponse.IsPublicProxy || anonymousResponse.IsResidentialProxy;
            isDatacenter |= anonymousResponse.IsHostingProvider;
            isTor = anonymousResponse.IsTorExitNode;
        }

        return new GeoipResult(country, city, asn, organization, isProxy, isDatacenter, isTor);
    }

    public AsnResult? LookupAsn(uint asn) => _asnIndex.TryGetValue(asn, out var organization)
        ? new AsnResult(organization, HostingOrganizations.IsHostingOrg(organization))
        : null;

    private static DatabaseReader? Open(string? path) => path is null ? null : new DatabaseReader(path);

    private static ConcurrentDictionary<uint, string> BuildAsnIndex(string path)
    {
        var values = new ConcurrentDictionary<uint, string>();
        using var reader = new Reader(path);
        foreach (var node in reader.FindAll<Dictionary<string, object>>(new InjectableValues(), 4096))
        {
            if (node.Data.TryGetValue("autonomous_system_number", out var numberValue) &&
                node.Data.TryGetValue("autonomous_system_organization", out var organizationValue) &&
                TryConvertAsn(numberValue, out var number) && organizationValue is string organization)
            {
                values.TryAdd(number, organization);
            }
        }

        return values;
    }

    private static bool TryConvertAsn(object value, out uint asn)
    {
        switch (value)
        {
            case uint unsigned:
                asn = unsigned;
                return true;
            case long signed when signed is >= 0 and <= uint.MaxValue:
                asn = (uint)signed;
                return true;
            case ulong wide when wide <= uint.MaxValue:
                asn = (uint)wide;
                return true;
            default:
                asn = 0;
                return false;
        }
    }

    public void Dispose()
    {
        _city?.Dispose();
        _asn?.Dispose();
        _anonymous?.Dispose();
    }
}

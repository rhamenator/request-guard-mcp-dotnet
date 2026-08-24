using RequestGuardMcp.Core.Classification;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Tests.Security;

public sealed class TlsAttestationTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string Ja3 = "72a589da586844d7f0818ce684948eea";
    private const string Ja4 = "t13d1516h2_8daaf6152771_e5627efa2ab1";

    [Fact]
    public void CreatesRustCompatibleRequestBoundVector()
    {
        var token = TlsAttestation.Create(Key, 1_700_000_000, "198.51.100.7", "GET", "/products", Ja3, Ja4, "envoy");

        Assert.Equal("v1:1700000000:192976122c9fbaa4cb8c2554be66f2439e020a7d470ac838f2a622b0c5829a49", token);
    }

    [Fact]
    public void RejectsTamperingExpiryAndExtremeTimestamp()
    {
        var token = TlsAttestation.Create(Key, 1_700_000_000, "198.51.100.7", "GET", "/products", Ja3, Ja4, "envoy");
        var config = new TlsFingerprintConfig { AttestationKey = Key, MaxAgeSeconds = 60 };
        var request = CreateRequest(token);
        request.Path = "/admin";

        Assert.False(TlsAttestation.VerifyAndNormalize(request, config, 1_700_000_030));
        Assert.False(TlsAttestation.VerifyAndNormalize(CreateRequest(token), config, 1_700_000_061));
        Assert.False(TlsAttestation.VerifyAndNormalize(CreateRequest($"v1:{long.MinValue}:" + new string('0', 64)), config, 1_700_000_000));
    }

    [Fact]
    public async Task ClassificationConsumesSecretAndAppliesKnownBadRule()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var token = TlsAttestation.Create(Key, now, "198.51.100.7", "GET", "/products", Ja3, Ja4, "envoy");
        var config = new AppConfig { Auth = { Enabled = false } };
        config.TlsFingerprints.AttestationKey = Key;
        config.TlsFingerprints.KnownBadJa3.Add(Ja3);
        var request = CreateRequest(token);
        await using var state = new AppState(config);

        var response = await ClassifyService.RunEphemeralAsync(state, request);

        Assert.True(request.TlsFingerprintVerified);
        Assert.Null(request.TlsFingerprintAttestation);
        Assert.Contains(response.Signals, signal => signal.Name == "tls_fingerprint_known_bad");
    }

    private static ClassifyRequest CreateRequest(string? token) => new()
    {
        Ip = "198.51.100.7",
        Path = "/products",
        Method = "GET",
        TlsJa3 = Ja3,
        TlsJa4 = Ja4,
        TlsFingerprintSource = "envoy",
        TlsFingerprintAttestation = token,
    };
}

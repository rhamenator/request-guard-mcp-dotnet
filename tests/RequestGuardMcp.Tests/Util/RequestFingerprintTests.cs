using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Tests.Util;

public class RequestFingerprintTests
{
    private static ClassifyRequest Request() => new()
    {
        Ip = "198.51.100.1",
        UserAgent = "Mozilla/5.0",
        Path = "/",
        Method = "GET",
    };

    [Fact]
    public void CacheKeysAreScopedToCaller()
    {
        var request = Request();

        var first = RequestFingerprint.Compute("caller:first", request);
        var second = RequestFingerprint.Compute("caller:second", request);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SameInputsProduceStableFingerprint()
    {
        var request = Request();

        var first = RequestFingerprint.Compute("caller", request);
        var second = RequestFingerprint.Compute("caller", request);

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnverifiedTlsFieldsDoNotAffectFingerprint()
    {
        var request = Request();
        request.TlsJa3 = "72a589da586844d7f0818ce684948eea";

        var withTls = RequestFingerprint.Compute("caller", request);

        request.TlsJa3 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var withDifferentTls = RequestFingerprint.Compute("caller", request);

        // TlsFingerprintVerified is always false until phase 5's attestation lands, so
        // unverified TLS values must not partition cache identity.
        Assert.Equal(withTls, withDifferentTls);
    }
}

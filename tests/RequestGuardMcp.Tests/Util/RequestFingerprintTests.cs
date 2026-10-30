using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Tests.Util;

public class RequestFingerprintTests
{
    private static string Fingerprint(string callerScope, ClassifyRequest request) =>
        RequestFingerprint.Compute(callerScope, request, "domain");

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

        var first = Fingerprint("caller:first", request);
        var second = Fingerprint("caller:second", request);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SameInputsProduceStableFingerprint()
    {
        var request = Request();

        var first = Fingerprint("caller", request);
        var second = Fingerprint("caller", request);

        Assert.Equal(first, second);
    }

    [Fact]
    public void AsciiVectorMatchesPinnedCurrentRustSerialization()
    {
        Assert.Equal(
            "8c8d5d23afe162c77e90cc4d60484f77f417250df55d3f9af48cb464e3fd01fa",
            RequestFingerprint.Compute("caller", Request(), "domain"));
    }

    [Fact]
    public void UnverifiedTlsFieldsDoNotAffectFingerprint()
    {
        var request = Request();
        request.TlsJa3 = "72a589da586844d7f0818ce684948eea";

        var withTls = Fingerprint("caller", request);

        request.TlsJa3 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var withDifferentTls = Fingerprint("caller", request);

        // Unverified TLS values must not partition cache identity.
        Assert.Equal(withTls, withDifferentTls);
    }
    [Fact]
    public void DelimitersCannotMoveDataBetweenRequestFields()
    {
        var first = Request();
        first.UserAgent = "browser|/admin";
        first.Path = "/";
        var second = Request();
        second.UserAgent = "browser";
        second.Path = "/admin|/";

        Assert.NotEqual(Fingerprint("caller", first), Fingerprint("caller", second));
    }

    [Fact]
    public void HeaderValuesAndUnrelatedHeadersDoNotPartitionCache()
    {
        var first = Request();
        first.Headers = new() { ["a"] = "x,b:y" };
        var second = Request();
        second.Headers = new() { ["a"] = "x", ["b"] = "y" };

        Assert.Equal(Fingerprint("caller", first), Fingerprint("caller", second));
    }

    [Theory]
    [InlineData("user_agent")]
    [InlineData("path")]
    [InlineData("method")]
    [InlineData("header_presence")]
    [InlineData("verified_tls")]
    public void RuleInputsPartitionCache(string field)
    {
        var request = Request();
        var original = Fingerprint("caller", request);
        switch (field)
        {
            case "user_agent": request.UserAgent = "GPTBot/1.0"; break;
            case "path": request.Path = "/admin"; break;
            case "method": request.Method = "CONNECT"; break;
            case "header_presence": request.Headers = new() { ["Accept"] = "text/html" }; break;
            case "verified_tls": request.TlsFingerprintVerified = true; request.TlsJa3 = "known-bad"; break;
        }

        Assert.NotEqual(original, Fingerprint("caller", request));
    }

    [Theory]
    [InlineData("ip")]
    [InlineData("body")]
    [InlineData("referer")]
    [InlineData("accept")]
    [InlineData("extra")]
    public void InputsIgnoredByRulesDoNotPartitionCache(string field)
    {
        var request = Request();
        var original = Fingerprint("caller", request);
        switch (field)
        {
            case "ip": request.Ip = "203.0.113.5"; break;
            case "body": request.BodySnippet = "payload"; break;
            case "referer": request.Referer = "https://example.test"; break;
            case "accept": request.Accept = "text/html"; break;
            case "extra": request.Extra = System.Text.Json.Nodes.JsonNode.Parse("""{"unicode":"雪","numeric":1.0}"""); break;
        }

        Assert.Equal(original, Fingerprint("caller", request));
    }

    [Fact]
    public void RequestMetadataDoesNotPartitionCache()
    {
        var request = Request();
        var original = Fingerprint("caller", request);
        request.RequestId = "another-id";
        request.Timestamp = "another-time";
        request.TlsFingerprintAttestation = "consumed-before-cache";

        Assert.Equal(original, Fingerprint("caller", request));
    }

    [Fact]
    public void QueryAndFragmentDoNotPartitionCache()
    {
        var request = Request();
        request.Path = "/search?value=one";
        var original = Fingerprint("caller", request);
        request.Path = "/search?value=two#fragment";

        Assert.Equal(original, Fingerprint("caller", request));
    }

    [Fact]
    public void CacheDomainPartitionsBuildAndTlsPolicy()
    {
        var tls = new RequestGuardMcp.Core.Configuration.TlsFingerprintConfig();
        var original = RequestFingerprint.ComputeCacheDomain(tls, "0.1.0", "commit-a");

        Assert.NotEqual(original, RequestFingerprint.ComputeCacheDomain(tls, "0.1.0", "commit-b"));
        tls.KnownBadJa3.Add("72A589DA586844D7F0818CE684948EEA");
        var changedPolicy = RequestFingerprint.ComputeCacheDomain(tls, "0.1.0", "commit-a");
        Assert.NotEqual(original, changedPolicy);

        tls.KnownBadJa3.Add("72a589da586844d7f0818ce684948eea");
        Assert.Equal(changedPolicy, RequestFingerprint.ComputeCacheDomain(tls, "0.1.0", "commit-a"));
    }

    [Fact]
    public void CacheDomainPartitionsRequestFingerprint()
    {
        var request = Request();

        Assert.NotEqual(
            RequestFingerprint.Compute("caller", request, "domain-a"),
            RequestFingerprint.Compute("caller", request, "domain-b"));
    }
}

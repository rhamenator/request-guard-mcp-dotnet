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

        Assert.NotEqual(RequestFingerprint.Compute("caller", first), RequestFingerprint.Compute("caller", second));
    }

    [Fact]
    public void HeaderValuesCannotImpersonateAdditionalHeaders()
    {
        var first = Request();
        first.Headers = new() { ["a"] = "x,b:y" };
        var second = Request();
        second.Headers = new() { ["a"] = "x", ["b"] = "y" };

        Assert.NotEqual(RequestFingerprint.Compute("caller", first), RequestFingerprint.Compute("caller", second));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("referer")]
    [InlineData("accept")]
    [InlineData("extra")]
    [InlineData("verified_tls")]
    public void AllDecisionInputsPartitionCache(string field)
    {
        var request = Request();
        var original = RequestFingerprint.Compute("caller", request);
        switch (field)
        {
            case "body": request.BodySnippet = "payload"; break;
            case "referer": request.Referer = "https://example.test"; break;
            case "accept": request.Accept = "text/html"; break;
            case "extra": request.Extra = System.Text.Json.Nodes.JsonNode.Parse("""{"risk":"high"}"""); break;
            case "verified_tls": request.TlsFingerprintVerified = true; request.TlsJa3 = "known-bad"; break;
        }

        Assert.NotEqual(original, RequestFingerprint.Compute("caller", request));
    }

    [Fact]
    public void ObjectOrderAndHeaderCaseDoNotPartitionCache()
    {
        var first = Request();
        first.Headers = new() { ["Accept"] = " text/html ", ["X-Test"] = "1" };
        first.Extra = System.Text.Json.Nodes.JsonNode.Parse("""{"b":[{"z":1,"a":2}],"a":0}""");
        var second = Request();
        second.Method = "get";
        second.Headers = new() { ["x-test"] = "1", ["accept"] = "text/html" };
        second.Extra = System.Text.Json.Nodes.JsonNode.Parse("""{"a":0,"b":[{"a":2,"z":1}]}""");

        Assert.Equal(RequestFingerprint.Compute("caller", first), RequestFingerprint.Compute("caller", second));
    }

    [Fact]
    public void RequestMetadataDoesNotPartitionCache()
    {
        var request = Request();
        var original = RequestFingerprint.Compute("caller", request);
        request.RequestId = "another-id";
        request.Timestamp = "another-time";
        request.TlsFingerprintAttestation = "consumed-before-cache";

        Assert.Equal(original, RequestFingerprint.Compute("caller", request));
    }

    [Fact]
    public void NullAndEmptyInputsRemainDistinct()
    {
        var request = Request();
        request.Accept = null;
        var original = RequestFingerprint.Compute("caller", request);
        request.Accept = "";

        Assert.NotEqual(original, RequestFingerprint.Compute("caller", request));
    }
}

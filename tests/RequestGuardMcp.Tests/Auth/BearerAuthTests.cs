using Microsoft.AspNetCore.Http;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Mcp.Auth;

namespace RequestGuardMcp.Tests.Auth;

public class BearerAuthTests
{
    private static HeaderDictionary AuthHeader(string value)
    {
        var headers = new HeaderDictionary { ["Authorization"] = value };
        return headers;
    }

    [Fact]
    public void ValidTokenIsAccepted()
    {
        var scope = BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer test-token-abc"), ["test-token-abc"]);

        Assert.StartsWith("caller:", scope, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidTokenIsForbidden()
    {
        var error = Assert.Throws<AppErrorException>(() =>
            BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer wrong-token"), ["test-token-abc"]));

        Assert.Equal(AppErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public void MissingHeaderIsUnauthenticated()
    {
        var error = Assert.Throws<AppErrorException>(() =>
            BearerAuth.AuthenticatedCacheScope(new HeaderDictionary(), ["test-token-abc"]));

        Assert.Equal(AppErrorKind.Unauthenticated, error.Kind);
    }

    [Fact]
    public void EmptyAllowedListRejectsEverything()
    {
        var error = Assert.Throws<AppErrorException>(() =>
            BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer any-token"), []));

        Assert.Equal(AppErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public void CacheScopeIsStableAndSeparatedPerToken()
    {
        var allowed = new[] { "token-one", "token-two" };

        var first = BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer token-one"), allowed);
        var repeated = BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer token-one"), allowed);
        var second = BearerAuth.AuthenticatedCacheScope(AuthHeader("Bearer token-two"), allowed);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("token-one", first, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyedScopeDiffersFromEphemeralScope()
    {
        var allowed = new[] { "test-token-abc" };
        var headers = AuthHeader("Bearer test-token-abc");

        var ephemeral = BearerAuth.AuthenticatedCacheScope(headers, allowed);
        var keyed = BearerAuth.AuthenticatedCacheScope(headers, allowed, "0123456789abcdef0123456789abcdef"u8.ToArray());

        Assert.NotEqual(ephemeral, keyed);
    }

    [Fact]
    public void LowercaseBearerSchemeIsAccepted()
    {
        var scope = BearerAuth.AuthenticatedCacheScope(AuthHeader("bearer test-token-abc"), ["test-token-abc"]);

        Assert.StartsWith("caller:", scope, StringComparison.Ordinal);
    }
}

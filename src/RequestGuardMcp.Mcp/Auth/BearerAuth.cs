using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using RequestGuardMcp.Core.Errors;

namespace RequestGuardMcp.Mcp.Auth;

/// <summary>
/// Validates the <c>Authorization</c> header against the configured bearer tokens and derives a
/// non-secret, server-controlled cache scope from the authenticated token. Ports src/auth.rs.
/// </summary>
public static class BearerAuth
{
    private static readonly Lazy<byte[]> EphemeralScopeKey = new(() =>
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    });

    /// <summary>
    /// Authenticates a caller and derives a per-caller cache scope. The scope prevents two
    /// separately provisioned MCP callers from sharing classification cache entries without
    /// ever placing a bearer token in a key: the digest is keyed (HMAC-SHA256) so the scope that
    /// lands in shared storage keys cannot be brute-forced back to the token. <paramref
    /// name="hmacKey"/> comes from <c>auth.cache_scope_hmac_key</c>, falling back to a
    /// process-local random key.
    /// </summary>
    public static string AuthenticatedCacheScope(IHeaderDictionary headers, IReadOnlyList<string> allowed, byte[]? hmacKey = null)
    {
        if (!headers.TryGetValue("Authorization", out var authValues) || authValues.Count == 0)
        {
            throw AppErrorException.Unauthenticated();
        }

        var authHeader = authValues[0];
        if (authHeader is null)
        {
            throw AppErrorException.Unauthenticated();
        }

        string? token = null;
        if (authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            token = authHeader["Bearer ".Length..];
        }
        else if (authHeader.StartsWith("bearer ", StringComparison.Ordinal))
        {
            token = authHeader["bearer ".Length..];
        }

        if (token is null)
        {
            throw AppErrorException.Unauthenticated();
        }

        if (!allowed.Contains(token, StringComparer.Ordinal))
        {
            throw AppErrorException.Forbidden();
        }

        var key = hmacKey ?? EphemeralScopeKey.Value;
        var digest = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(token)));
        return $"caller:{digest}";
    }
}

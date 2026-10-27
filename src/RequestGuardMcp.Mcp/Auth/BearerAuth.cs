using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Util;

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
        if (!headers.TryGetValue("Authorization", out var authValues) || authValues.Count != 1)
        {
            throw AppErrorException.Unauthenticated();
        }

        var authHeader = authValues[0];
        if (authHeader is null)
        {
            throw AppErrorException.Unauthenticated();
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw AppErrorException.Unauthenticated();
        }

        var token = authHeader["Bearer".Length..].TrimStart(' ');
        if (!BearerToken.IsValid(token))
        {
            throw AppErrorException.Unauthenticated();
        }

        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var matched = false;
        foreach (var candidate in allowed)
        {
            matched |= CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(candidate), tokenBytes);
        }

        if (!matched)
        {
            throw AppErrorException.Forbidden();
        }

        var key = hmacKey ?? EphemeralScopeKey.Value;
        var digest = Convert.ToHexStringLower(HMACSHA256.HashData(key, tokenBytes));
        return $"caller:{digest}";
    }
}

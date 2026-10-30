using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Models.Request;

namespace RequestGuardMcp.Core.Util;

/// <summary>
/// Computes a stable cache key from the authenticated caller scope, classification build/policy
/// domain, and fields consumed by the rule engine. TLS values participate only after request-bound
/// server verification. Metadata ignored by classification is deliberately excluded.
/// </summary>
public static class RequestFingerprint
{
    public static string Compute(string callerScope, ClassifyRequest request, string cacheDomain)
    {
        JsonArray? headerPresence = request.Headers is { } values
            ? new JsonArray(
                values.Keys.Any(name => name.Equals("accept", StringComparison.OrdinalIgnoreCase)),
                values.Keys.Any(name => name.Equals("accept-language", StringComparison.OrdinalIgnoreCase)))
            : null;
        var pathOnly = request.Path?.Split(['?', '#'], 2)[0];

        var input = new JsonObject
        {
            ["cache_domain"] = cacheDomain,
            ["caller_scope"] = callerScope,
            ["user_agent"] = request.UserAgent,
            ["path"] = pathOnly,
            ["method"] = request.Method is { } method ? AsciiCase(method, upper: true) : null,
            ["header_presence"] = headerPresence,
            ["verified_tls"] = request.TlsFingerprintVerified ? new JsonObject
            {
                ["ja3"] = request.TlsJa3,
                ["ja4"] = request.TlsJa4,
                ["source"] = request.TlsFingerprintSource,
            } : null,
        };

        return Sha256(input.ToJsonString());
    }

    public static string ComputeCacheDomain(TlsFingerprintConfig tls, string version, string gitCommit)
    {
        var ja3 = tls.KnownBadJa3
            .Select(value => TlsAttestation.NormalizeJa3(value))
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(value => JsonValue.Create(value))
            .ToArray();
        var ja4 = tls.KnownBadJa4
            .Select(value => TlsAttestation.NormalizeJa4(value))
            .Where(value => value is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(value => JsonValue.Create(value))
            .ToArray();
        var input = new JsonObject
        {
            ["schema"] = "classification-v3",
            ["version"] = version,
            ["git_commit"] = gitCommit,
            ["known_bad_ja3"] = new JsonArray(ja3),
            ["known_bad_ja4"] = new JsonArray(ja4),
        };
        return Sha256(input.ToJsonString());
    }

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string AsciiCase(string value, bool upper) => string.Create(value.Length, (value, upper), static (span, state) =>
    {
        for (var index = 0; index < span.Length; index++)
        {
            var character = state.value[index];
            span[index] = state.upper && character is >= 'a' and <= 'z' ? (char)(character - 32) :
                !state.upper && character is >= 'A' and <= 'Z' ? (char)(character + 32) : character;
        }
    });
}

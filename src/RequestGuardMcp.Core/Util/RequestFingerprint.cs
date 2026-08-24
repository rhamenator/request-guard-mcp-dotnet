using System.Security.Cryptography;
using System.Text;
using RequestGuardMcp.Core.Models.Request;

namespace RequestGuardMcp.Core.Util;

/// <summary>
/// Computes a stable cache key from the authenticated caller scope and every caller field used
/// by the rule engine. Ports src/util/hashing.rs's <c>request_fingerprint</c>. TLS values
/// participate only after the server verifies a short-lived request binding.
/// </summary>
public static class RequestFingerprint
{
    public static string Compute(string callerScope, ClassifyRequest request)
    {
        var canonicalHeaders = (request.Headers ?? [])
            .Select(pair => $"{pair.Key.ToLowerInvariant()}:{pair.Value.Trim()}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var verified = request.TlsFingerprintVerified;
        var input = string.Join('|',
            callerScope,
            request.Ip ?? "",
            request.UserAgent ?? "",
            request.Path ?? "",
            (request.Method ?? "").ToUpperInvariant(),
            string.Join(",", canonicalHeaders),
            verified ? "true" : "false",
            verified ? request.TlsJa3 ?? "" : "",
            verified ? request.TlsJa4 ?? "" : "",
            verified ? request.TlsFingerprintSource ?? "" : "");

        return Sha256Hex(input);
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}

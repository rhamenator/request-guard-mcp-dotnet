using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Models.Request;

namespace RequestGuardMcp.Core.Util;

/// <summary>Creates and verifies short-lived, request-bound HMAC attestations for JA3/JA4 metadata.</summary>
public static class TlsAttestation
{
    public const string Version = "v1";

    public static string? NormalizeJa3(string? value)
    {
        var candidate = value?.Trim().ToLowerInvariant();
        return candidate is { Length: 32 } && candidate.All(Uri.IsHexDigit) ? candidate : null;
    }

    public static string? NormalizeJa4(string? value)
    {
        var candidate = value?.Trim().ToLowerInvariant();
        var sections = candidate?.Split('_');
        return sections is { Length: 3 } && sections[0].Length == 10 && sections[1].Length == 12 && sections[2].Length == 12 &&
               sections[0].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) &&
               sections[1].All(Uri.IsHexDigit) && sections[2].All(Uri.IsHexDigit)
            ? candidate
            : null;
    }

    public static string? NormalizeSource(string? value)
    {
        var candidate = value?.Trim().ToLowerInvariant();
        return candidate is { Length: > 0 and <= 32 } &&
               candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? candidate : null;
    }

    public static string? Create(string key, long issuedAt, string clientIp, string method, string path, string? ja3, string? ja4, string source)
    {
        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            return null;
        }

        ja3 = NormalizeJa3(ja3);
        ja4 = NormalizeJa4(ja4);
        source = NormalizeSource(source)!;
        if ((ja3 is null && ja4 is null) || source is null)
        {
            return null;
        }

        var values = new[] { Version, issuedAt.ToString(CultureInfo.InvariantCulture), clientIp.Trim().ToLowerInvariant(), method.Trim().ToUpperInvariant(), path, ja3 ?? "", ja4 ?? "", source };
        if (values.Any(value => value.IndexOfAny(['\n', '\r', '\0']) >= 0))
        {
            return null;
        }

        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(string.Join('\n', values)));
        return $"{Version}:{issuedAt.ToString(CultureInfo.InvariantCulture)}:{Convert.ToHexStringLower(signature)}";
    }

    public static bool VerifyAndNormalize(ClassifyRequest request, TlsFingerprintConfig config, long now)
    {
        request.TlsFingerprintVerified = false;
        if (request.TlsFingerprintAttestation is not { } token)
        {
            return false;
        }

        var parts = token.Trim().Split(':');
        if (parts is not [Version, var timestamp, var signature] ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var issuedAt) ||
            signature.Length != 64 || !signature.All(Uri.IsHexDigit) || issuedAt < 0 ||
            (issuedAt > now ? issuedAt - now : now - issuedAt) > config.MaxAgeSeconds)
        {
            return false;
        }

        var ja3 = NormalizeJa3(request.TlsJa3);
        var ja4 = NormalizeJa4(request.TlsJa4);
        var source = NormalizeSource(request.TlsFingerprintSource);
        if (source is null)
        {
            return false;
        }

        foreach (var key in new[] { config.AttestationKey, config.PreviousAttestationKey })
        {
            var expected = key is null ? null : Create(key, issuedAt, request.Ip ?? "", request.Method ?? "GET", request.Path ?? "", ja3, ja4, source);
            var expectedSignature = expected?.Split(':')[2];
            if (expectedSignature is not null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), Convert.FromHexString(expectedSignature)))
            {
                request.TlsJa3 = ja3;
                request.TlsJa4 = ja4;
                request.TlsFingerprintSource = source;
                request.TlsFingerprintVerified = true;
                return true;
            }
        }

        return false;
    }
}

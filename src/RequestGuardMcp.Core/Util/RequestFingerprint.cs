using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Models.Request;

namespace RequestGuardMcp.Core.Util;

/// <summary>
/// Computes a stable, unambiguous cache key from the authenticated caller scope and all
/// decision inputs. TLS values participate only after request-bound server verification.
/// Request IDs, timestamps and the consumed attestation are deliberately excluded.
/// </summary>
public static class RequestFingerprint
{
    public static string Compute(string callerScope, ClassifyRequest request)
    {
        JsonObject? headers = null;
        if (request.Headers is { } values)
        {
            headers = new JsonObject();
            foreach (var (name, value) in values)
            {
                headers[AsciiCase(name, upper: false)] = value.Trim();
            }
        }

        var input = new JsonObject
        {
            ["caller_scope"] = callerScope,
            ["ip"] = request.Ip,
            ["user_agent"] = request.UserAgent,
            ["path"] = request.Path,
            ["method"] = request.Method is { } method ? AsciiCase(method, upper: true) : null,
            ["headers"] = headers,
            ["body_snippet"] = request.BodySnippet,
            ["referer"] = request.Referer,
            ["accept"] = request.Accept,
            ["extra"] = request.Extra?.DeepClone(),
            ["verified_tls"] = request.TlsFingerprintVerified ? new JsonObject
            {
                ["ja3"] = request.TlsJa3,
                ["ja4"] = request.TlsJa4,
                ["source"] = request.TlsFingerprintSource,
            } : null,
        };

        // Sort objects recursively so caller-controlled dictionary insertion order cannot
        // partition an otherwise identical request. Arrays retain their semantic order.
        var canonical = Canonicalize(input)!.ToJsonString();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => KeyValuePair.Create(pair.Key, Canonicalize(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Canonicalize).ToArray()),
        _ => node?.DeepClone(),
    };

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

using System.Text.RegularExpressions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Signals;

namespace RequestGuardMcp.Core.Engines;

/// <summary>
/// Rule-based signal extraction engine. Ports src/engines/rules.rs, including rules for
/// server-verified TLS fingerprints.
/// </summary>
public sealed partial class RuleEngine
{
    private static readonly string[] ModernBrowserMarkers = ["chrome/", "crios/", "firefox/", "fxios/", "safari/", "edg/"];
    [GeneratedRegex(
        "(GPTBot|ChatGPT-User|Claude-Web|anthropic-ai|Bytespider|CCBot|cohere-ai|DuckAssistBot|" +
        "FacebookBot|Google-Extended|ImagesiftBot|PerplexityBot|Scrapy|python-httpx|python-requests|" +
        "aiohttp|curl/|wget/|libwww-perl|Go-http-client|Java/|okhttp)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AiBotUserAgent();

    [GeneratedRegex(
        "(scrapy|beautifulsoup|mechanize|selenium|phantom|puppeteer|playwright|headless|crawler|spider|bot|scraper)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ScrapingUserAgent();

    [GeneratedRegex(
        @"(\.env|/admin|/api/internal|/wp-admin|/phpmyadmin|\.git/|/etc/passwd|/proc/|/debug|/actuator|/swagger|/graphql|/config)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SensitivePath();

    /// <summary>Run all rules against a classify request and return a signal set.</summary>
    public static SignalSet Evaluate(ClassifyRequest request, TlsFingerprintConfig? tlsConfig = null)
    {
        var signals = new SignalSet();

        if (request.UserAgent is { } userAgent)
        {
            EvaluateUserAgent(userAgent, signals);
        }

        if (request.Path is { } path)
        {
            EvaluatePath(path, signals);
        }

        if (request.Headers is { } headers)
        {
            EvaluateHeaders(headers, signals);
        }

        if (request.Method is { } method)
        {
            EvaluateMethod(method, signals);
        }

        EvaluateTlsFingerprint(request, tlsConfig, signals);

        return signals;
    }

    private static void EvaluateTlsFingerprint(ClassifyRequest request, TlsFingerprintConfig? config, SignalSet signals)
    {
        if (!request.TlsFingerprintVerified)
        {
            return;
        }

        var knownBad = request.TlsJa3 is { } ja3 && config?.KnownBadJa3.Contains(ja3, StringComparer.Ordinal) == true ||
                       request.TlsJa4 is { } ja4 && config?.KnownBadJa4.Contains(ja4, StringComparer.Ordinal) == true;
        if (knownBad)
        {
            signals.Add(new Signal("tls_fingerprint_known_bad", 1.0, 0.85, SignalSource.RuleEngine, "Verified TLS fingerprint matches the configured threat set"));
        }

        var userAgent = request.UserAgent ?? "";
        var claimsModernBrowser = userAgent.Contains("Mozilla/5.0", StringComparison.OrdinalIgnoreCase) &&
                                  ModernBrowserMarkers.Any(marker => userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var transport = request.TlsJa4 is { Length: >= 3 } ? request.TlsJa4[..3] : null;
        if (claimsModernBrowser && transport is not ("t12" or "t13" or "q12" or "q13"))
        {
            signals.Add(new Signal("ua_tls_profile_mismatch", 1.0, 0.45, SignalSource.RuleEngine, "Browser user-agent conflicts with the verified JA4 transport profile"));
        }
    }

    private static void EvaluateUserAgent(string userAgent, SignalSet signals)
    {
        if (AiBotUserAgent().IsMatch(userAgent))
        {
            signals.Add(new Signal("ua_ai_bot", 1.0, 0.8, SignalSource.RuleEngine, "User-agent matches known AI bot pattern"));
        }
        else if (ScrapingUserAgent().IsMatch(userAgent))
        {
            signals.Add(new Signal("ua_scraper", 1.0, 0.7, SignalSource.RuleEngine, "User-agent matches scraping tool pattern"));
        }

        if (userAgent.Length == 0)
        {
            signals.Add(new Signal("ua_empty", 1.0, 0.4, SignalSource.RuleEngine, "Empty user-agent string"));
        }

        // Check for raw version strings without browser context (e.g. "python-requests/2.x").
        if (userAgent.Contains('/', StringComparison.Ordinal) &&
            !userAgent.Contains("mozilla", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new Signal("ua_non_browser", 0.7, 0.3, SignalSource.RuleEngine, "Non-browser user-agent string"));
        }
    }

    private static void EvaluatePath(string path, SignalSet signals)
    {
        if (SensitivePath().IsMatch(path))
        {
            signals.Add(new Signal("path_sensitive", 1.0, 0.6, SignalSource.RuleEngine, "Request targets a sensitive path"));
        }

        // Bulk / enumeration pattern: many path segments or numeric IDs.
        var segments = path.Trim('/').Split('/', StringSplitOptions.None).Length;
        if (segments > 8)
        {
            signals.Add(new Signal("path_deep", 0.6, 0.2, SignalSource.RuleEngine, "Unusually deep path traversal"));
        }
    }

    private static void EvaluateHeaders(IReadOnlyDictionary<string, string> headers, SignalSet signals)
    {
        var keysLower = headers.Keys.Select(key => key.ToLowerInvariant()).ToHashSet();

        if (!keysLower.Contains("accept"))
        {
            signals.Add(new Signal("header_missing_accept", 0.6, 0.2, SignalSource.RuleEngine, "Missing Accept header"));
        }

        if (!keysLower.Contains("accept-language"))
        {
            signals.Add(new Signal("header_missing_accept_language", 0.5, 0.15, SignalSource.RuleEngine, "Missing Accept-Language header"));
        }
    }

    private static void EvaluateMethod(string method, SignalSet signals)
    {
        switch (method.ToUpperInvariant())
        {
            case "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS":
                break;
            default:
                signals.Add(new Signal("method_unusual", 0.7, 0.2, SignalSource.RuleEngine, "Unusual HTTP method"));
                break;
        }
    }
}

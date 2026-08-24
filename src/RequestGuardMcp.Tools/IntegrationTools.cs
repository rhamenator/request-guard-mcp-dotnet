using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RequestGuardMcp.Core.Classification;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

internal static class ToolResult
{
    public static JsonNode? Json<T>(T value) => JsonSerializer.SerializeToNode(value, McpJson.Options);

    public static async Task<T> UpstreamAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (AppErrorException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw AppErrorException.Upstream(error.Message);
        }
    }

    public static T Upstream<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (AppErrorException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw AppErrorException.Upstream(error.Message);
        }
    }
}

public sealed class FeedbackTool : IMcpTool
{
    public string Name => "feedback";
    public string Description => "Submit feedback on a classification";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<FeedbackRequest>(parameters);
        if (!state.Config.Features.EnableFeedback || !state.Postgres.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("feedback requires PostgreSQL persistence and enable_feedback");
        }

        var verdict = request.CorrectVerdict.ToLowerInvariant();
        if (verdict is not ("allow" or "block" or "flag" or "challenge"))
        {
            throw AppErrorException.InvalidRequest("correct_verdict must be allow, block, flag, or challenge");
        }

        if (await ToolResult.UpstreamAsync(() => state.Postgres.GetDecisionAsync(request.RequestId, cancellationToken)).ConfigureAwait(false) is null)
        {
            throw AppErrorException.InvalidRequest($"classification request id '{request.RequestId}' was not found");
        }

        var id = await ToolResult.UpstreamAsync(() => state.Postgres.RecordFeedbackAsync(request.RequestId, verdict, request.Notes, request.Reporter, cancellationToken)).ConfigureAwait(false);
        return ToolResult.Json(new FeedbackResponse(true, id, "Feedback accepted. Thank you."));
    }
}

public sealed class ReplayDecisionTool : IMcpTool
{
    public string Name => "replay_decision";
    public string Description => "Replay a previous decision";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<ReplayRequest>(parameters);
        if (!state.Postgres.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("decision replay requires PostgreSQL persistence");
        }

        if (request.Deterministic == false)
        {
            throw AppErrorException.InvalidRequest("only deterministic replay is supported");
        }

        var original = await ToolResult.UpstreamAsync(() => state.Postgres.GetDecisionAsync(request.RequestId, cancellationToken)).ConfigureAwait(false)
            ?? throw AppErrorException.InvalidRequest($"classification request id '{request.RequestId}' was not found");
        original.Request.RequestId = $"replay-{Guid.NewGuid()}";
        var replayed = await ClassifyService.RunEphemeralAsync(state, original.Request, cancellationToken).ConfigureAwait(false);
        var equivalent = original.Response.Verdict == replayed.Verdict && original.Response.Score.Equals(replayed.Score) &&
                         original.Response.Confidence == replayed.Confidence && original.Response.ThreatCategory == replayed.ThreatCategory &&
                         JsonSerializer.Serialize(original.Response.Signals, McpJson.Options) == JsonSerializer.Serialize(replayed.Signals, McpJson.Options);
        return ToolResult.Json(new ReplayResponse(request.RequestId, ToolResult.Json(original.Response), replayed, equivalent));
    }
}

public sealed class EnrichIpTool : IMcpTool
{
    public string Name => "enrich_ip";
    public string Description => "Enrich an IP address with geo/ASN data";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<EnrichIpRequest>(parameters);
        var ip = NetUtil.ParseIp(request.Ip) ?? throw AppErrorException.InvalidRequest("ip is not a valid IP address");
        var isPrivate = NetUtil.IsPrivate(ip);
        if (!state.Config.Features.EnableEnrichment || (!isPrivate && !state.Geoip.HasIpDatabase && !state.Reputation.IsConfigured))
        {
            throw AppErrorException.IntegrationUnavailable("IP enrichment requires enable_enrichment and a MaxMind database or Redis reputation registry");
        }

        var enrichment = state.Geoip.HasIpDatabase ? ToolResult.Upstream(() => state.Geoip.LookupIp(ip)) : new();
        var reputation = await ToolResult.UpstreamAsync(() => state.Reputation.LookupIpAsync(ip.ToString(), cancellationToken)).ConfigureAwait(false);
        var geoRisk = isPrivate ? 0.0 : enrichment.IsTor || enrichment.IsProxy ? 0.9 : enrichment.IsDatacenter ? 0.6 : 0.1;
        return ToolResult.Json(new EnrichIpResponse(ip.ToString(), enrichment.Country, enrichment.City, enrichment.Asn, enrichment.Org,
            enrichment.IsProxy, enrichment.IsDatacenter, enrichment.IsTor, Math.Max(geoRisk, reputation.Score)));
    }
}

public sealed class EnrichAsnTool : IMcpTool
{
    public string Name => "enrich_asn";
    public string Description => "Enrich an ASN with organization data";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<EnrichAsnRequest>(parameters);
        if (request.Asn == 0)
        {
            throw AppErrorException.InvalidRequest("asn must be greater than zero");
        }

        if (!state.Config.Features.EnableEnrichment || (!state.Geoip.HasAsnDatabase && !state.Reputation.IsConfigured))
        {
            throw AppErrorException.IntegrationUnavailable("ASN enrichment requires enable_enrichment and a MaxMind ASN database or Redis reputation registry");
        }

        var enrichment = ToolResult.Upstream(() => state.Geoip.LookupAsn(request.Asn));
        var reputation = await ToolResult.UpstreamAsync(() => state.Reputation.LookupAsnAsync(request.Asn, cancellationToken)).ConfigureAwait(false);
        var hosting = enrichment?.IsHosting == true;
        return ToolResult.Json(new EnrichAsnResponse(request.Asn, enrichment?.Organization, null, Math.Max(hosting ? 0.6 : 0.1, reputation.Score), hosting));
    }
}

public sealed class EnrichUaTool : IMcpTool
{
    private static readonly string[] Bots = ["bot", "crawler", "spider", "scrapy", "headless", "curl/", "wget/", "python-requests", "httpx"];
    public string Name => "enrich_ua";
    public string Description => "Enrich a user-agent string";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<EnrichUaRequest>(parameters);
        if (!state.Config.Features.EnableEnrichment)
        {
            throw AppErrorException.IntegrationUnavailable("user-agent enrichment is disabled");
        }

        var bot = Bots.FirstOrDefault(marker => request.UserAgent.Contains(marker, StringComparison.OrdinalIgnoreCase));
        var browser = BrowserName(request.UserAgent, bot);
        var os = OperatingSystemName(request.UserAgent);
        var device = bot is not null ? "crawler" : request.UserAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ? "smartphone" : browser is null ? null : "pc";
        return Task.FromResult(ToolResult.Json(new EnrichUaResponse(request.UserAgent, browser, os, device, bot is not null, bot is null ? null : browser ?? bot, bot is null ? 0.1 : 0.8)));
    }

    private static string? BrowserName(string ua, string? bot) => bot is not null ? bot :
        ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
        ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
        ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
        ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" : null;

    private static string? OperatingSystemName(string ua) =>
        ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
        ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
        ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
        ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
        ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : null;
}

public sealed class ThreatLookupTool : IMcpTool
{
    public string Name => "threat_lookup";
    public string Description => "Look up a threat indicator";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<ThreatLookupRequest>(parameters);
        if (!state.Redis.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("threat lookup requires Redis");
        }

        var indicator = request.Indicator.Trim();
        if (indicator.Length == 0)
        {
            throw AppErrorException.InvalidRequest("indicator cannot be empty");
        }
        var type = request.IndicatorType?.ToLowerInvariant() ?? (IPAddress.TryParse(indicator, out _) ? "ip" : Uri.TryCreate(indicator, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? "url" : "domain");
        if (type is not ("ip" or "domain" or "url" or "hash" or "asn"))
        {
            throw AppErrorException.InvalidRequest("type must be ip, domain, url, hash, or asn");
        }
        var normalized = NormalizeIndicator(type, indicator);
        var record = await ToolResult.UpstreamAsync(() => state.Redis.ThreatLookupAsync(type, normalized, cancellationToken)).ConfigureAwait(false);
        return ToolResult.Json(new ThreatLookupResponse(request.Indicator, record is not null, record?.ThreatType, record?.Severity, record?.Source, record?.LastSeen));
    }

    private static string NormalizeIndicator(string type, string value) => type switch
    {
        "ip" => IPAddress.TryParse(value, out var ip) ? ip.ToString() : throw AppErrorException.InvalidRequest("indicator is not a valid IP address"),
        "domain" or "hash" => value.ToLowerInvariant(),
        "asn" => uint.TryParse(value.StartsWith("AS", StringComparison.OrdinalIgnoreCase) ? value[2..] : value, NumberStyles.None, CultureInfo.InvariantCulture, out var asn)
            ? asn.ToString(CultureInfo.InvariantCulture) : throw AppErrorException.InvalidRequest("indicator is not a valid ASN"),
        _ => value,
    };
}

public sealed class CanaryEvalTool : IMcpTool
{
    public string Name => "canary_eval";
    public string Description => "Evaluate a canary token";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<CanaryEvalRequest>(parameters);
        if (!state.Redis.IsAvailable)
        {
            throw AppErrorException.IntegrationUnavailable("canary evaluation requires Redis");
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw AppErrorException.InvalidRequest("token cannot be empty");
        }
        var record = await ToolResult.UpstreamAsync(() => state.Redis.CanaryLookupAsync(request.Token, request.Context, cancellationToken)).ConfigureAwait(false);
        return ToolResult.Json(new CanaryEvalResponse(request.Token, record is not null, record?.CanaryId, record?.Metadata));
    }
}

public sealed partial class AbusePatternMatchTool : IMcpTool
{
    private sealed record AbusePattern(string Name, string Category, Regex Regex);
    private static readonly AbusePattern[] Patterns =
    [
        new("sql_injection", "injection", SqlInjection()), new("xss", "injection", Xss()),
        new("path_traversal", "traversal", PathTraversal()), new("ai_prompt_injection", "ai_attack", PromptInjection()),
        new("sensitive_data_probe", "data_extraction", SensitiveData()),
    ];

    public string Name => "abuse_pattern_match";
    public string Description => "Match abuse patterns in text";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<AbusePatternMatchRequest>(parameters);
        var categories = request.Categories ?? [];
        var matches = Patterns.Where(pattern => categories.Count == 0 || categories.Contains(pattern.Category, StringComparer.Ordinal))
            .Select(pattern => (Pattern: pattern, Match: pattern.Regex.Match(request.Text)))
            .Where(result => result.Match.Success)
            .Select(result => new PatternMatch(result.Pattern.Name, result.Pattern.Category, 0.9, result.Match.Value[..Math.Min(64, result.Match.Value.Length)]))
            .ToList();
        return Task.FromResult(ToolResult.Json(new AbusePatternMatchResponse(matches.Count > 0, matches, Math.Clamp(matches.Count * 0.3, 0.0, 1.0))));
    }

    [GeneratedRegex(@"(\bSELECT\b.*\bFROM\b|\bUNION\b.*\bSELECT\b|\bDROP\b.*\bTABLE\b)", RegexOptions.IgnoreCase)] private static partial Regex SqlInjection();
    [GeneratedRegex(@"(<script[\s\S]*?>|javascript:|on\w+\s*=)", RegexOptions.IgnoreCase)] private static partial Regex Xss();
    [GeneratedRegex(@"(\.\./|\.\.\\|%2e%2e)", RegexOptions.IgnoreCase)] private static partial Regex PathTraversal();
    [GeneratedRegex("(ignore previous instructions|you are now|forget your|disregard all|act as if you)", RegexOptions.IgnoreCase)] private static partial Regex PromptInjection();
    [GeneratedRegex("(ssn|social security|credit card|cvv|api.?key|password|secret)", RegexOptions.IgnoreCase)] private static partial Regex SensitiveData();
}

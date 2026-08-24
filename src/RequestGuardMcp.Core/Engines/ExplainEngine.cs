using System.Globalization;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Models.Signals;

namespace RequestGuardMcp.Core.Engines;

/// <summary>Generates a human-readable explanation for a classification result. Ports src/engines/explain.rs.</summary>
public static class ExplainEngine
{
    public static ExplainResponse ExplainSignals(string requestId, SignalSet signals, double score, string verdict)
    {
        var totalWeight = signals.Signals.Sum(s => s.Weight);

        var factors = signals.Signals
            .Select(s => new ExplanationFactor(
                s.Name,
                totalWeight > 0.0 ? s.Contribution / totalWeight * 100.0 : 0.0,
                s.Description))
            .ToList();

        return new ExplainResponse(
            requestId,
            BuildExplanation(score, verdict, factors),
            factors,
            BuildRecommendations(score, verdict, signals));
    }

    private static string BuildExplanation(double score, string verdict, List<ExplanationFactor> factors)
    {
        var pct = Math.Round(score * 100.0);
        if (factors.Count == 0)
        {
            return $"No suspicious signals detected. Score: {pct:F0}%. Verdict: {verdict}.";
        }

        var top = factors.Take(3).Select(f => $"{f.Name} ({f.Contribution.ToString("F0", CultureInfo.InvariantCulture)}%)");
        return $"Score {pct:F0}% → verdict '{verdict}'. Top contributing signals: {string.Join(", ", top)}.";
    }

    private static List<string> BuildRecommendations(double score, string verdict, SignalSet signals)
    {
        var recommendations = new List<string>();

        if (score >= 0.75)
        {
            recommendations.Add("Consider blocking this request at the WAF or load balancer level.");
            recommendations.Add("Review and update blocklists for matching patterns.");
        }
        else if (score >= 0.40)
        {
            recommendations.Add("Consider serving a CAPTCHA challenge.");
            recommendations.Add("Monitor for repeated requests from this source.");
        }

        if (signals.Signals.Any(s => s.Name == "ua_ai_bot"))
        {
            recommendations.Add("Add robots.txt disallow rules for identified AI bots.");
        }

        if (verdict == "allow" && recommendations.Count == 0)
        {
            recommendations.Add("No action required. Request appears legitimate.");
        }

        return recommendations;
    }
}

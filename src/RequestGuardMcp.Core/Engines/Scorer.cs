using RequestGuardMcp.Core.Models.Enums;
using RequestGuardMcp.Core.Models.Signals;

namespace RequestGuardMcp.Core.Engines;

/// <summary>Compute a final verdict and threat category from a set of signals. Ports src/engines/scorer.rs.</summary>
public sealed class Scorer
{
    public const double ThresholdBlock = 0.75;
    public const double ThresholdFlag = 0.55;
    public const double ThresholdChallenge = 0.40;
    public const double ThresholdAllow = 0.0;

    public static ScorerResult Score(SignalSet signals)
    {
        var score = signals.AggregateScore();
        return new ScorerResult(
            Score: score,
            Verdict: DetermineVerdict(score),
            Confidence: DetermineConfidence(signals),
            ThreatCategory: DetermineThreatCategory(signals));
    }

    private static Verdict DetermineVerdict(double score) => score switch
    {
        >= ThresholdBlock => Verdict.Block,
        >= ThresholdFlag => Verdict.Flag,
        >= ThresholdChallenge => Verdict.Challenge,
        _ => Verdict.Allow,
    };

    private static Confidence DetermineConfidence(SignalSet signals)
    {
        var count = signals.Signals.Count;
        var maxWeight = signals.Signals.Count == 0 ? 0.0 : signals.Signals.Max(s => s.Weight);

        return (count, maxWeight) switch
        {
            ( >= 3, >= 0.7) => Confidence.High,
            _ when count >= 2 || maxWeight >= 0.5 => Confidence.Medium,
            _ when count >= 1 => Confidence.Low,
            _ => Confidence.VeryLow,
        };
    }

    private static ThreatCategory DetermineThreatCategory(SignalSet signals)
    {
        var names = signals.Signals.Select(s => s.Name).ToList();

        if (names.Contains("ua_ai_bot"))
        {
            return ThreatCategory.AiScraping;
        }

        if (names.Contains("ua_scraper"))
        {
            return ThreatCategory.Scraping;
        }

        if (names.Any(name => name.StartsWith("ua_", StringComparison.Ordinal)))
        {
            return ThreatCategory.BotTraffic;
        }

        if (names.Contains("path_sensitive"))
        {
            return ThreatCategory.DataExtraction;
        }

        return names.Count == 0 ? ThreatCategory.None : ThreatCategory.Unknown;
    }
}

public sealed record ScorerResult(double Score, Verdict Verdict, Confidence Confidence, ThreatCategory ThreatCategory);

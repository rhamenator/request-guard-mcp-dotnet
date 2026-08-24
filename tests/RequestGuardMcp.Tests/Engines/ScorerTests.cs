using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Models.Enums;
using RequestGuardMcp.Core.Models.Signals;

namespace RequestGuardMcp.Tests.Engines;

public class ScorerTests
{
    [Fact]
    public void HighScoreYieldsBlockOrFlag()
    {
        var signals = new SignalSet();
        signals.Add(new Signal("ua_ai_bot", 1.0, 0.8, SignalSource.RuleEngine, "test"));
        signals.Add(new Signal("ua_non_browser", 0.7, 0.3, SignalSource.RuleEngine, "test"));

        var result = Scorer.Score(signals);

        Assert.True(result.Verdict is Verdict.Block or Verdict.Flag);
    }

    [Fact]
    public void EmptySignalsYieldAllow()
    {
        var result = Scorer.Score(new SignalSet());

        Assert.Equal(Verdict.Allow, result.Verdict);
        Assert.Equal(0.0, result.Score);
        Assert.Equal(Confidence.VeryLow, result.Confidence);
        Assert.Equal(ThreatCategory.None, result.ThreatCategory);
    }

    [Fact]
    public void AiScrapingThreatCategoryFollowsAiBotSignal()
    {
        var signals = new SignalSet();
        signals.Add(new Signal("ua_ai_bot", 1.0, 0.8, SignalSource.RuleEngine, "test"));

        var result = Scorer.Score(signals);

        Assert.Equal(ThreatCategory.AiScraping, result.ThreatCategory);
    }
}

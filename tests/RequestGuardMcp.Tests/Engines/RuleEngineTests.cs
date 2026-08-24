using RequestGuardMcp.Core.Engines;
using RequestGuardMcp.Core.Models.Request;

namespace RequestGuardMcp.Tests.Engines;

public class RuleEngineTests
{
    [Fact]
    public void DetectsGptBot()
    {
        var request = new ClassifyRequest { UserAgent = "GPTBot/1.0" };

        var signals = RuleEngine.Evaluate(request);

        Assert.Contains(signals.Signals, s => s.Name == "ua_ai_bot");
    }

    [Fact]
    public void CleanRequestHasNoHighSignals()
    {
        var request = new ClassifyRequest
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
            Path = "/index.html",
        };

        var signals = RuleEngine.Evaluate(request);
        var score = signals.AggregateScore();

        Assert.True(score < 0.5, $"clean request score too high: {score}");
    }

    [Fact]
    public void EmptyUserAgentIsFlagged()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { UserAgent = "" });

        Assert.Contains(signals.Signals, s => s.Name == "ua_empty");
    }

    [Fact]
    public void SensitivePathIsFlagged()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Path = "/wp-admin/login.php" });

        Assert.Contains(signals.Signals, s => s.Name == "path_sensitive");
    }

    [Fact]
    public void MissingAcceptHeadersAreFlagged()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Headers = new Dictionary<string, string>() });

        Assert.Contains(signals.Signals, s => s.Name == "header_missing_accept");
        Assert.Contains(signals.Signals, s => s.Name == "header_missing_accept_language");
    }

    [Fact]
    public void UnusualMethodIsFlagged()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Method = "TRACE" });

        Assert.Contains(signals.Signals, s => s.Name == "method_unusual");
    }

    [Fact]
    public void StandardMethodIsNotFlagged()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Method = "POST" });

        Assert.DoesNotContain(signals.Signals, s => s.Name == "method_unusual");
    }
}

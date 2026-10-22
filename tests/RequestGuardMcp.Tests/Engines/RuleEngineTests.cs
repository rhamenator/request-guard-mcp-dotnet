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
    [Theory]
    [InlineData("masscan/1.3")]
    [InlineData("Nikto")]
    [InlineData("sqlmap/1.0")]
    [InlineData("Apache-HttpClient/4.5")]
    [InlineData("HeadlessChrome/120")]
    public void ScannerUserAgentsProduceScraperSignal(string userAgent)
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { UserAgent = userAgent });

        var signal = Assert.Single(signals.Signals, signal => signal.Name == "ua_scraper");
        Assert.Equal(0.7, signal.Weight);
    }

    [Theory]
    [InlineData("masscanary browser")]
    [InlineData("niktopolis")]
    [InlineData("sqlmapper")]
    [InlineData("myhttpclient")]
    public void ScannerNamesRequireWordBoundaries(string userAgent)
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { UserAgent = userAgent });

        Assert.DoesNotContain(signals.Signals, signal => signal.Name == "ua_scraper");
    }

    [Theory]
    [InlineData("/%2eenv")]
    [InlineData("/%252eenv")]
    [InlineData("/%25252eenv")]
    [InlineData("/%2525252eenv")]
    [InlineData("/%61dmin")]
    [InlineData("/%2Fadmin")]
    [InlineData(@"\admin")]
    [InlineData("/%255cadmin")]
    [InlineData("/%FF/admin")]
    [InlineData("/%ZZ/admin")]
    [InlineData("/internal")]
    [InlineData("/internal?x=1")]
    [InlineData("/internal/metrics")]
    [InlineData("/xmlrpc.php")]
    [InlineData("/xmlrpc.php?x=1")]
    public void SensitivePathsIncludeEncodedAndScannerTargets(string path)
    {
        var request = new ClassifyRequest { Path = path };
        var signals = RuleEngine.Evaluate(request);

        var signal = Assert.Single(signals.Signals, signal => signal.Name == "path_sensitive");
        Assert.Equal(0.6, signal.Weight);
        Assert.Equal(path, request.Path);
    }

    [Theory]
    [InlineData("/welcome")]
    [InlineData("/internalized")]
    [InlineData("/xmlrpc.phpish")]
    [InlineData("/%252525252eenv")]
    [InlineData("/%")]
    [InlineData("/%2")]
    public void OrdinaryOrOutOfBudgetPathsDoNotMatch(string path)
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Path = path });

        Assert.DoesNotContain(signals.Signals, signal => signal.Name == "path_sensitive");
    }

    [Fact]
    public void EncodedSeparatorsContributeToPathDepth()
    {
        var signals = RuleEngine.Evaluate(new ClassifyRequest { Path = "/a%2Fb%2Fc%2Fd%2Fe%2Ff%2Fg%2Fh%2Fi" });

        Assert.Contains(signals.Signals, signal => signal.Name == "path_deep");
    }
}

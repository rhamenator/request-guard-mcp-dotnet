using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Tools;

public class BoundedWorkTests
{
    private static AppState State() => new(new AppConfig { Auth = new AuthConfig { Enabled = false } });

    private static async Task<JsonNode?> AbuseAsync(string text)
    {
        var parameters = new JsonObject { ["text"] = text };
        return await new AbusePatternMatchTool().CallAsync(State(), parameters, CancellationToken.None);
    }

    [Theory]
    [InlineData("SELECT ")]          // many starts of the SQL pattern, never a FROM
    [InlineData("<script ")]         // many starts of the lazy XSS pattern, never a closing >
    [InlineData("UNION ")]
    public async Task NearMissInputsFinishQuickly(string unit)
    {
        var text = string.Concat(Enumerable.Repeat(unit, TextLimitsForTests.AbuseChars / unit.Length));
        var watch = Stopwatch.StartNew();

        var result = await AbuseAsync(text);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
        Assert.NotNull(result);
    }

    [Fact]
    public async Task OversizedAbuseTextIsRejected()
    {
        var text = new string('a', TextLimitsForTests.AbuseChars + 1);
        await Assert.ThrowsAsync<AppErrorException>(() => AbuseAsync(text));
    }

    [Fact]
    public async Task KnownAttackStringsStillMatch()
    {
        var result = await AbuseAsync("1 UNION ALL SELECT password FROM users; <script>alert(1)</script> ../../etc/passwd");
        var names = result!["patterns"]!.AsArray().Select(m => m!["pattern"]!.GetValue<string>()).ToArray();
        Assert.Contains("sql_injection", names);
        Assert.Contains("xss", names);
        Assert.Contains("path_traversal", names);
    }

    [Fact]
    public async Task OversizedUserAgentIsRejected()
    {
        var parameters = new JsonObject { ["user_agent"] = new string('x', 1025) };
        await Assert.ThrowsAsync<AppErrorException>(() => new EnrichUaTool().CallAsync(State(), parameters, CancellationToken.None));
    }

    [Fact]
    public async Task AdversarialWindowsUserAgentIsBounded()
    {
        var ua = string.Concat(Enumerable.Repeat("Windows ", 128));
        var watch = Stopwatch.StartNew();
        _ = await new EnrichUaTool().CallAsync(State(), new JsonObject { ["user_agent"] = ua }, CancellationToken.None);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    private sealed class BlockingTool : IMcpTool
    {
        public string Name => "blocking";
        public string Description => "does CPU-bound work before its first await";

        public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
        {
            Thread.Sleep(TimeSpan.FromSeconds(4));   // synchronous, as a regex-heavy tool would be
            return Task.FromResult<JsonNode?>(new JsonObject());
        }
    }

    [Fact]
    public async Task ASynchronouslyBusyToolStillHitsItsTimeoutAndReleasesTheSlot()
    {
        var registry = new ToolRegistry();
        registry.Register(new BlockingTool());
        var dispatcher = new McpDispatcher(registry, NullLogger<McpDispatcher>.Instance);
        var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false }, Limits = new LimitsConfig { PerToolTimeoutSecs = 1, GlobalConcurrency = 1 } });
        var watch = Stopwatch.StartNew();

        var response = await dispatcher.ProcessMessageAsync("""{"jsonrpc":"2.0","id":1,"method":"blocking"}""", state, "public", CancellationToken.None);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"the 1 s timeout did not fire: {watch.Elapsed}");
        Assert.Contains("TIMEOUT", response!, StringComparison.Ordinal);
        Assert.Equal(1, state.Semaphore.CurrentCount);   // the concurrency slot came back
    }

    private static class TextLimitsForTests
    {
        public const int AbuseChars = 16 * 1024;
    }
}

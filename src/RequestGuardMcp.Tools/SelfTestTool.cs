using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Health;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Enums;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using ClassifyService = RequestGuardMcp.Core.Classification.ClassifyService;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>self_test</c> MCP tool. Ports src/tools/self_test.rs.</summary>
public sealed class SelfTestTool : IMcpTool
{
    public string Name => "self_test";

    public string Description => "Run internal test suite";

    public async Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        _ = McpJson.ParamsOrDefault<SelfTestRequest>(parameters);
        var results = new List<SelfTestResult>
        {
            await ClassifyGptBotAsync(state, cancellationToken).ConfigureAwait(false),
            await ClassifyCleanBrowserAsync(state, cancellationToken).ConfigureAwait(false),
            await HealthCheckSelfTestAsync(state, cancellationToken).ConfigureAwait(false),
        };

        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);

        var response = new SelfTestResponse(passed, failed, Skipped: 0, results, failed == 0 ? "pass" : "fail");
        return JsonSerializer.SerializeToNode(response, McpJson.Options);
    }

    private static async Task<SelfTestResult> ClassifyGptBotAsync(AppState state, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var dummy = new ClassifyRequest { UserAgent = "GPTBot/1.0", Path = "/", Method = "GET", RequestId = "self_test_1" };
        var response = await ClassifyService.RunEphemeralAsync(state, dummy, cancellationToken).ConfigureAwait(false);
        var passed = response.Verdict is Verdict.Block or Verdict.Flag;
        return new SelfTestResult("classify_gptbot", passed, passed ? null : $"expected block/flag, got {response.Verdict}", (ulong)stopwatch.ElapsedMilliseconds);
    }

    private static async Task<SelfTestResult> ClassifyCleanBrowserAsync(AppState state, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var dummy = new ClassifyRequest
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            Path = "/index.html",
            Method = "GET",
            Headers = new Dictionary<string, string> { ["accept"] = "text/html", ["accept-language"] = "en-US,en;q=0.9" },
            RequestId = "self_test_2",
        };
        var response = await ClassifyService.RunEphemeralAsync(state, dummy, cancellationToken).ConfigureAwait(false);
        var passed = response.Verdict == Verdict.Allow;
        return new SelfTestResult("classify_clean_browser", passed, passed ? null : $"expected allow, got {response.Verdict}", (ulong)stopwatch.ElapsedMilliseconds);
    }

    private static async Task<SelfTestResult> HealthCheckSelfTestAsync(AppState state, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var health = await HealthCheck.RunAsync(state, cancellationToken).ConfigureAwait(false);
        var passed = health.Status == "healthy";
        return new SelfTestResult("health_check", passed, passed ? null : $"unexpected status: {health.Status}", (ulong)stopwatch.ElapsedMilliseconds);
    }
}

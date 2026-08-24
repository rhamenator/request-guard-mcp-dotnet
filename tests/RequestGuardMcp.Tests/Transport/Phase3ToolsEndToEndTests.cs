using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

/// <summary>End-to-end coverage of the phase 3 tools, dispatched exactly as the Host composes them.</summary>
public class Phase3ToolsEndToEndTests
{
    private static (McpDispatcher Dispatcher, AppState State) NewDispatcher(AppConfig? config = null)
    {
        var registry = new ToolRegistry();
        registry.Register(new HealthTool());
        registry.Register(new ModelInfoTool(registry));
        registry.Register(new ClassifyTool());
        registry.Register(new BatchClassifyTool());
        registry.Register(new ExplainTool());
        registry.Register(new ScoreBreakdownTool());
        registry.Register(new ValidatePayloadTool());
        registry.Register(new FeatureFlagsTool());
        registry.Register(new RedactPreviewTool());
        registry.Register(new ConfigSnapshotTool());
        registry.Register(new SelfTestTool());
        registry.Register(new WarmupTool());

        var dispatcher = new McpDispatcher(registry, NullLogger<McpDispatcher>.Instance);
        var state = new AppState(config ?? new AppConfig { Auth = new AuthConfig { Enabled = false } });
        return (dispatcher, state);
    }

    private static async Task<JsonNode> DispatchAsync(McpDispatcher dispatcher, AppState state, string method, string paramsJson = "{}")
    {
        var response = await dispatcher.ProcessMessageAsync(
            $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""",
            state, "public", CancellationToken.None);
        return JsonNode.Parse(response!)!;
    }

    [Fact]
    public async Task ClassifyGptBotIsBlockedOrFlagged()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "classify", """{"user_agent":"GPTBot/1.0","path":"/"}""");

        var verdict = node["result"]!["verdict"]!.GetValue<string>();
        Assert.True(verdict is "block" or "flag");
    }

    [Fact]
    public async Task ClassifyCleanBrowserIsAllowed()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "classify",
            """{"user_agent":"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36","path":"/index.html","headers":{"accept":"text/html","accept-language":"en-US"}}""");

        Assert.Equal("allow", node["result"]!["verdict"]!.GetValue<string>());
    }

    [Fact]
    public async Task ClassifyIsCachedByFingerprint()
    {
        var (dispatcher, state) = NewDispatcher();
        const string paramsJson = """{"user_agent":"GPTBot/1.0","path":"/","request_id":"same"}""";

        var first = await DispatchAsync(dispatcher, state, "classify", paramsJson);
        var second = await DispatchAsync(dispatcher, state, "classify", paramsJson);

        Assert.Equal(first["result"]!["score"]!.GetValue<double>(), second["result"]!["score"]!.GetValue<double>());
    }

    [Fact]
    public async Task BatchClassifyProcessesAllItems()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "batch_classify",
            """{"items":[{"user_agent":"GPTBot/1.0"},{"user_agent":"Mozilla/5.0 Chrome"}]}""");

        Assert.Equal(2, node["result"]!["total"]!.GetValue<int>());
        Assert.Equal(2, node["result"]!["processed"]!.GetValue<int>());
    }

    [Fact]
    public async Task BatchClassifyOverLimitIsRejected()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { MaxBatchSize = 1 },
        };
        var (dispatcher, state) = NewDispatcher(config);

        var node = await DispatchAsync(dispatcher, state, "batch_classify",
            """{"items":[{"user_agent":"a"},{"user_agent":"b"}]}""");

        Assert.Equal("BATCH_TOO_LARGE", node["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task BatchClassifyHonorsDisabledFeatureFlag()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Features = new FeatureConfig { EnableBatch = false },
        };
        var (dispatcher, state) = NewDispatcher(config);

        var node = await DispatchAsync(dispatcher, state, "batch_classify", """{"items":[]}""");

        Assert.Equal("INTEGRATION_UNAVAILABLE", node["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExplainFallsBackWithoutAClassifyRequest()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "explain", """{"classification":{"not":"a classify request"}}""");

        Assert.Empty(node["result"]!["factors"]!.AsArray());
    }

    [Fact]
    public async Task ExplainProducesFactorsFromARealClassifyRequest()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "explain",
            """{"classification":{"user_agent":"GPTBot/1.0","path":"/"}}""");

        Assert.NotEmpty(node["result"]!["factors"]!.AsArray());
    }

    [Fact]
    public async Task FeatureFlagsReturnsBatchClassifyEnabled()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "feature_flags");

        Assert.True(node["result"]!["flags"]!["batch_classify"]!.GetValue<bool>());
    }

    [Fact]
    public async Task WarmupWarmsRuleEngineScorerAndCache()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "warmup");

        var warmed = node["result"]!["warmed"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("rule_engine", warmed);
        Assert.Contains("scorer", warmed);
        Assert.Contains("cache", warmed);
    }

    [Fact]
    public async Task RedactPreviewRedactsDefaultSensitiveFields()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "redact_preview", """{"payload":{"password":"hunter2","name":"alice"}}""");

        Assert.Equal("[REDACTED]", node["result"]!["redacted"]!["password"]!.GetValue<string>());
        Assert.Contains("password", node["result"]!["fields_redacted"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public async Task ValidatePayloadRejectsClassifyWithNoIdentifyingField()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "validate_payload", """{"tool":"classify","payload":{}}""");

        Assert.False(node["result"]!["valid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ValidatePayloadAcceptsClassifyWithUserAgent()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "validate_payload", """{"tool":"classify","payload":{"user_agent":"x"}}""");

        Assert.True(node["result"]!["valid"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ScoreBreakdownReportsThresholds()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "score_breakdown");

        Assert.Equal(0.75, node["result"]!["threshold_block"]!.GetValue<double>());
    }

    [Fact]
    public async Task ConfigSnapshotReportsAuthTokenCount()
    {
        var config = new AppConfig { Auth = new AuthConfig { Tokens = ["a", "b"] } };
        var (dispatcher, state) = NewDispatcher(config);

        var node = await DispatchAsync(dispatcher, state, "config_snapshot");

        Assert.Equal(2, node["result"]!["snapshot"]!["auth"]!["token_count"]!.GetValue<int>());
    }

    [Fact]
    public async Task SelfTestReportsOverallPass()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "self_test");

        Assert.Equal("pass", node["result"]!["overall_status"]!.GetValue<string>());
        Assert.Equal(3, node["result"]!["passed"]!.GetValue<int>());
    }

    [Fact]
    public async Task ModelInfoNowReportsAllPhase3Tools()
    {
        var (dispatcher, state) = NewDispatcher();

        var node = await DispatchAsync(dispatcher, state, "model_info");

        Assert.Equal(12, node["result"]!["tool_count"]!.GetValue<int>());
        Assert.Equal("classify", node["result"]!["tools"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Classify multiple requests at once", node["result"]!["tools"]![2]!["description"]!.GetValue<string>());
        Assert.NotNull(node["result"]!["build_info"]!["rust_version"]);
        Assert.Null(node["result"]!["build_info"]!["runtime_version"]);
    }
}

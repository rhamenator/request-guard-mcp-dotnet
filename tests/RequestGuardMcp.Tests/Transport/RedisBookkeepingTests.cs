using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

public class RedisBookkeepingTests
{
    private const string Request = """{"jsonrpc":"2.0","id":41,"method":"classify","params":{"user_agent":"GPTBot/1.0","request_id":"record-test"}}""";

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task StalledRecordsCannotPreventSuccessfulClassification(bool stallStart, bool stallFinish)
    {
        var redis = new BookkeepingRedis(stallStart, stallFinish);
        await using var state = State(redis);
        var dispatcher = Dispatcher(new ClassifyTool());
        try
        {
            var response = await dispatcher.ProcessMessageAsync(Request, state, "test", CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(900));
            var node = JsonNode.Parse(response!)!;
            Assert.Equal(41, node["id"]!.GetValue<int>());
            Assert.Null(node["error"]);
            Assert.Contains(node["result"]!["signals"]!.AsArray(), signal => signal!["name"]!.GetValue<string>() == "ua_ai_bot");
            Assert.Equal(stallStart ? 0 : 1, redis.FinishCalls);
            Assert.Equal(1, redis.CacheReads);
            Assert.Equal(state.Config.Limits.GlobalConcurrency, state.Semaphore.CurrentCount);
        }
        finally
        {
            redis.Release();
        }
    }

    [Fact]
    public async Task ToolTimeoutPlusStalledFinishHasOnlyHalfSecondBookkeepingTail()
    {
        var redis = new BookkeepingRedis(stallStart: false, stallFinish: true);
        await using var state = State(redis);
        var slow = new StalledTool();
        try
        {
            var response = await Dispatcher(slow).ProcessMessageAsync(Request, state, "test", CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(1900));
            var error = JsonNode.Parse(response!)!["error"]!;
            Assert.Equal(-504, error["code"]!.GetValue<int>());
            Assert.Equal("TIMEOUT", error["message"]!.GetValue<string>());
            Assert.Equal(1, redis.FinishCalls);
            Assert.Equal(state.Config.Limits.GlobalConcurrency, state.Semaphore.CurrentCount);
        }
        finally
        {
            redis.Release();
            slow.Completion.TrySetResult(null);
        }
    }

    [Fact]
    public async Task CancellationDuringStartDoesNotDispatchToolAndReleasesPermit()
    {
        var redis = new BookkeepingRedis(stallStart: true, stallFinish: false);
        await using var state = State(redis);
        using var cancelled = new CancellationTokenSource();
        var dispatch = Dispatcher(new ClassifyTool()).ProcessMessageAsync(Request, state, "test", cancelled.Token);
        try
        {
            await redis.StartInvoked.Task;
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
            Assert.Equal(0, redis.CacheReads);
            Assert.Equal(0, redis.FinishCalls);
            Assert.Equal(state.Config.Limits.GlobalConcurrency, state.Semaphore.CurrentCount);
        }
        finally
        {
            redis.Release();
        }
    }

    [Fact]
    public async Task HealthyRecordsStillCompleteNormally()
    {
        var redis = new BookkeepingRedis(stallStart: false, stallFinish: false);
        await using var state = State(redis);
        var response = await Dispatcher(new ClassifyTool()).ProcessMessageAsync(Request, state, "test", CancellationToken.None);
        Assert.Null(JsonNode.Parse(response!)!["error"]);
        Assert.Equal(1, redis.FinishCalls);
    }

    private static AppState State(BookkeepingRedis redis)
    {
        var state = new AppState(new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { ClassifyTimeoutSecs = 1 },
        });
        state.AttachIntegrations(redis: redis);
        return state;
    }

    private static McpDispatcher Dispatcher(IMcpTool tool)
    {
        var registry = new ToolRegistry();
        registry.Register(tool);
        return new McpDispatcher(registry, NullLogger<McpDispatcher>.Instance);
    }

    private sealed class StalledTool : IMcpTool
    {
        public TaskCompletionSource<JsonNode?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Name => "classify";
        public string Description => "Cancellation-ignoring tool regression";
        public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken) => Completion.Task;
    }

    // Both record methods intentionally ignore cancellation, matching the concrete Redis
    // client's transaction waits. Cache behavior stays healthy to isolate bookkeeping.
    private sealed class BookkeepingRedis(bool stallStart, bool stallFinish) : IRedisClient
    {
        private readonly TaskCompletionSource<string> _start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartInvoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int FinishCalls { get; private set; }
        public int CacheReads { get; private set; }
        public bool IsAvailable => true;
        public Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default)
        {
            StartInvoked.TrySetResult();
            return stallStart ? _start.Task : Task.FromResult("operation-test");
        }

        public Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default)
        {
            FinishCalls++;
            return stallFinish ? _finish.Task : Task.CompletedTask;
        }

        public void Release() { _start.TrySetResult("late-operation"); _finish.TrySetResult(); }
        public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default)
        {
            CacheReads++;
            return Task.FromResult<JsonNode?>(null);
        }

        public Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default) => NullRedisClient.Instance.ThreatLookupAsync(indicatorType, indicator, cancellationToken);
        public Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default) => NullRedisClient.Instance.StoreThreatAsync(indicatorType, indicator, record, cancellationToken);
        public Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default) => NullRedisClient.Instance.CanaryLookupAsync(token, context, cancellationToken);
        public Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default) => NullRedisClient.Instance.RegisterCanaryAsync(token, record, cancellationToken);
        public Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default) => NullRedisClient.Instance.QueueStatsAsync(requested, cancellationToken);
    }
}

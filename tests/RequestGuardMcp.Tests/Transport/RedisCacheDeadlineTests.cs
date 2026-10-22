using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Classification;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Tests.Transport;

public class RedisCacheDeadlineTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OptionalCacheStallsDoNotPreventClassification(bool stallRead, bool stallWrite)
    {
        var redis = new CacheRedis(stallRead, stallWrite);
        await using var state = new AppState(new AppConfig());
        state.AttachIntegrations(redis: redis);
        try
        {
            var result = await ClassifyService.RunAsync(state, Request(), "caller")
                .WaitAsync(TimeSpan.FromMilliseconds(1500));
            Assert.Equal("cache-deadline", result.RequestId);
            Assert.Contains(result.Signals, signal => signal.Name == "ua_ai_bot");
            Assert.True(redis.WriteStarted.Task.IsCompleted);
        }
        finally
        {
            redis.Release();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellationIsNotMistakenForAnOptionalCacheFailure(bool duringRead)
    {
        var redis = new CacheRedis(duringRead, !duringRead);
        await using var state = new AppState(new AppConfig());
        state.AttachIntegrations(redis: redis);
        using var cancelled = new CancellationTokenSource();
        var pending = ClassifyService.RunAsync(state, Request(), "caller", cancelled.Token);
        try
        {
            await (duringRead ? redis.ReadStarted.Task : redis.WriteStarted.Task);
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            redis.Release();
        }
    }

    private static ClassifyRequest Request() => new() { UserAgent = "GPTBot/1.0", RequestId = "cache-deadline" };

    // Deliberately ignore cancellation to exercise the application wait boundary.
    private sealed class CacheRedis(bool stallRead, bool stallWrite) : IRedisClient
    {
        private readonly TaskCompletionSource<JsonNode?> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _write = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsAvailable => true;
        public Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            return stallRead ? _read.Task : Task.FromResult<JsonNode?>(null);
        }

        public Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            return stallWrite ? _write.Task : Task.CompletedTask;
        }

        public void Release()
        {
            _read.TrySetResult(null);
            _write.TrySetResult();
        }

        public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default) => Task.FromResult("cache-test");
        public Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default) => NullRedisClient.Instance.ThreatLookupAsync(indicatorType, indicator, cancellationToken);
        public Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default) => NullRedisClient.Instance.StoreThreatAsync(indicatorType, indicator, record, cancellationToken);
        public Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default) => NullRedisClient.Instance.CanaryLookupAsync(token, context, cancellationToken);
        public Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default) => NullRedisClient.Instance.RegisterCanaryAsync(token, record, cancellationToken);
        public Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default) => NullRedisClient.Instance.QueueStatsAsync(requested, cancellationToken);
    }
}

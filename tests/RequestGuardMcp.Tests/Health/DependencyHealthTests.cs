using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Health;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Tests.Health;

public class DependencyHealthTests
{
    private sealed class SlowRedis(TaskCompletionSource<bool> release) : IRedisClient
    {
        public int Pings;
        public bool IsAvailable => true;

        public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Pings);
            return await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static AppState StateWith(SlowRedis redis, int deadlineSecs = 3)
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Redis = new RedisConfig { Url = "redis://localhost:6379" },
            Limits = new LimitsConfig { HealthDeadlineSecs = deadlineSecs },
        };
        var state = new AppState(config);
        state.AttachIntegrations(redis: redis);
        return state;
    }

    [Fact]
    public void LivenessDoesNotTouchDependencies()
    {
        var redis = new SlowRedis(new TaskCompletionSource<bool>());
        var state = StateWith(redis);

        var health = HealthCheck.Liveness(state);

        Assert.Equal("healthy", health.Status);
        Assert.Equal(0, redis.Pings);
        Assert.DoesNotContain("redis", health.Checks.Keys);
    }

    [Fact]
    public async Task ConcurrentReadinessChecksShareOneProbe()
    {
        var release = new TaskCompletionSource<bool>();
        var redis = new SlowRedis(release);
        var state = StateWith(redis);

        var calls = Enumerable.Range(0, 50).Select(_ => state.DependencyHealth.GetAsync(state, CancellationToken.None)).ToArray();
        release.SetResult(true);
        var results = await Task.WhenAll(calls);

        Assert.All(results, result => Assert.Equal("healthy", result.Status));
        Assert.Equal(1, redis.Pings);
    }

    [Fact]
    public async Task ResultIsReusedBrieflyThenRefreshed()
    {
        var release = new TaskCompletionSource<bool>();
        release.SetResult(true);
        var redis = new SlowRedis(release);
        var clock = new ManualClock();
        var cache = new DependencyHealthCache(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), clock);
        var state = StateWith(redis);

        await cache.GetAsync(state, CancellationToken.None);
        await cache.GetAsync(state, CancellationToken.None);
        Assert.Equal(1, redis.Pings);

        clock.Advance(TimeSpan.FromSeconds(3));
        await cache.GetAsync(state, CancellationToken.None);
        Assert.Equal(2, redis.Pings);
    }

    [Fact]
    public async Task AHangingDependencyIsReportedUnhealthyAtTheDeadline()
    {
        var redis = new SlowRedis(new TaskCompletionSource<bool>());   // never answers
        var state = StateWith(redis, deadlineSecs: 1);

        var started = DateTimeOffset.UtcNow;
        var result = await state.DependencyHealth.GetAsync(state, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("unhealthy", result.Status);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}

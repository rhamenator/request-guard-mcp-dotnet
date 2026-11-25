using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Models.Enums;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Tools;
using RequestGuardMcp.Host;
using RequestGuardMcp.Integrations;

namespace RequestGuardMcp.Tests.Integrations;

public class PostgresRetentionTests
{
    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakePostgres(params (int Decisions, int Feedback)[] batches) : IPostgresClient
    {
        private int _next;
        public List<(DateTimeOffset Cutoff, int Batch)> Calls { get; } = [];
        public bool IsAvailable => true;
        public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<PurgeResult> PurgeExpiredAsync(DateTimeOffset olderThan, int batchSize, CancellationToken cancellationToken = default)
        {
            Calls.Add((olderThan, batchSize));
            var (decisions, feedback) = batches[_next++];
            return Task.FromResult(new PurgeResult(decisions, feedback));
        }

        public Task RecordDecisionAsync(string callerScope, ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PersistedDecision?> GetDecisionAsync(string callerScope, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> RecordFeedbackAsync(string callerScope, string requestId, string correctVerdict, string? notes, string? reporter, PersistedDecision expected, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DriftSummary> DriftSummaryAsync(string callerScope, DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CalibrationSummary> CalibrationSummaryAsync(string callerScope, DateTimeOffset start, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task RetentionRunsBatchesUntilOneIsShort()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        var postgres = new FakePostgres((1000, 0), (1000, 40), (250, 0));
        var config = new PostgresConfig { RetentionDays = 30, RetentionBatchSize = 1000 };
        var service = new PostgresRetentionService(postgres, config, new FakeClock(now), NullLogger<PostgresRetentionService>.Instance);

        var total = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2250, total.Decisions);
        Assert.Equal(40, total.Feedback);
        Assert.Equal(3, postgres.Calls.Count);
        Assert.All(postgres.Calls, call => Assert.Equal(now.AddDays(-30), call.Cutoff));
        Assert.All(postgres.Calls, call => Assert.Equal(1000, call.Batch));
    }

    [Fact]
    public async Task RetentionKeepsDrainingFeedbackAfterTheDecisionsAreGone()
    {
        // Expired feedback on decisions whose timestamp was refreshed: decisions return 0 while feedback fills a batch.
        var postgres = new FakePostgres((0, 1000), (0, 1000), (0, 7));
        var config = new PostgresConfig { RetentionDays = 30, RetentionBatchSize = 1000 };
        var service = new PostgresRetentionService(postgres, config, new FakeClock(DateTimeOffset.UnixEpoch), NullLogger<PostgresRetentionService>.Instance);

        var total = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, postgres.Calls.Count);
        Assert.Equal(2007, total.Feedback);
        Assert.Equal(0, total.Decisions);
    }

    [Theory]
    [InlineData("days")]
    [InlineData("rows")]
    [InlineData("batch")]
    [InlineData("interval")]
    public void RetentionSettingsAreValidated(string field)
    {
        var config = new AppConfig { Auth = new AuthConfig { Enabled = false } };
        switch (field)
        {
            case "days": config.Postgres.RetentionDays = -1; break;
            case "rows": config.Postgres.MaxRowsPerCaller = -1; break;
            case "batch": config.Postgres.RetentionBatchSize = 0; break;
            case "interval": config.Postgres.RetentionIntervalMinutes = 0; break;
        }

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void RetentionIsOffByDefault()
    {
        var config = new PostgresConfig();
        Assert.Equal(0, config.RetentionDays);
        Assert.Equal(0, config.MaxRowsPerCaller);
    }

    [Theory]
    [InlineData("notes", 2001)]
    [InlineData("reporter", 201)]
    public async Task FeedbackFreeTextIsBounded(string field, int length)
    {
        var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        state.AttachIntegrations(postgres: new FakePostgres());
        var parameters = new JsonObject { ["request_id"] = "r1", ["correct_verdict"] = "allow", [field] = new string('x', length) };

        var error = await Assert.ThrowsAsync<AppErrorException>(() =>
            new FeedbackTool().CallScopedAsync(state, parameters, "caller", CancellationToken.None));
        Assert.Contains("characters", error.Message, StringComparison.Ordinal);
    }

    private static ClassifyResponse Response(string id) =>
        new(id, Verdict.Allow, 0.1, Confidence.High, ThreatCategory.BotTraffic, [], 1, "test");

    /// <summary>Runs only when RG_TEST_POSTGRES_URL points at a disposable PostgreSQL database.</summary>
    [Fact]
    public async Task RealDatabaseHonoursTheCallerCapAndRetention()
    {
        var url = Environment.GetEnvironmentVariable("RG_TEST_POSTGRES_URL");
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var scope = "retention-test-" + Guid.NewGuid().ToString("N");
        await using var client = await PostgresClient.ConnectAsync(new PostgresConfig { Url = url, MaxRowsPerCaller = 3 });
        for (var i = 0; i < 5; i++)
        {
            await client.RecordDecisionAsync(scope, new ClassifyRequest { Path = "/" + i }, Response("req-" + i));
            await Task.Delay(5);
        }

        await using var source = NpgsqlDataSource.Create(url);
        await using var count = source.CreateCommand("SELECT count(*) FROM mcp_decisions WHERE caller_scope = $1");
        count.Parameters.Add(new NpgsqlParameter { Value = scope });
        Assert.Equal(3L, await count.ExecuteScalarAsync());
        Assert.Null(await client.GetDecisionAsync(scope, "req-0"));      // oldest were evicted
        Assert.NotNull(await client.GetDecisionAsync(scope, "req-4"));   // newest kept

        var removed = await client.PurgeExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2);
        Assert.Equal(2, removed.Decisions);                              // bounded by the batch size
        var rest = await client.PurgeExpiredAsync(DateTimeOffset.UtcNow.AddMinutes(1), 2000);
        Assert.True(removed.Decisions + rest.Decisions >= 3);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    /// <summary>Runs only when RG_TEST_POSTGRES_URL points at a disposable PostgreSQL database.</summary>
    [Fact]
    public async Task ConcurrentWritersCannotExceedTheCallerCap()
    {
        var url = Environment.GetEnvironmentVariable("RG_TEST_POSTGRES_URL");
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        var scope = "cap-race-" + Guid.NewGuid().ToString("N");
        await using var client = await PostgresClient.ConnectAsync(new PostgresConfig { Url = url, MaxRowsPerCaller = 1, MaxConnections = 48 });
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            client.RecordDecisionAsync(scope, new ClassifyRequest { Path = "/" + i }, Response("race-" + scope + "-" + i))));

        await using var source = NpgsqlDataSource.Create(url);
        await using var count = source.CreateCommand("SELECT count(*) FROM mcp_decisions WHERE caller_scope = $1");
        count.Parameters.Add(new NpgsqlParameter { Value = scope });
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }
}

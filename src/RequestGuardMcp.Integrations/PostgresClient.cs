using System.Text.Json;
using NpgsqlTypes;
using Npgsql;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;

namespace RequestGuardMcp.Integrations;

/// <summary>
/// PostgreSQL-backed decision persistence, feedback, drift, and calibration reporting. Ports
/// src/integrations/postgres.rs's <c>PostgresClient</c> (the <c>postgres-integration</c>
/// feature's inner module) using Npgsql. Only ever constructed via <see cref="ConnectAsync"/>
/// when a URL is configured — the disabled state is represented separately by
/// <see cref="Core.Integrations.NullPostgresClient"/>.
/// </summary>
public sealed class PostgresClient : IPostgresClient, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    private PostgresClient(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public static async Task<PostgresClient> ConnectAsync(PostgresConfig config, CancellationToken cancellationToken = default)
    {
        var builder = new NpgsqlDataSourceBuilder(config.Url);
        builder.ConnectionStringBuilder.MaxPoolSize = config.MaxConnections;
        builder.ConnectionStringBuilder.Timeout = config.ConnectTimeoutSecs;
        var dataSource = builder.Build();
        var client = new PostgresClient(dataSource);
        try
        {
            await client.InitializeSchemaAsync(cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool IsAvailable => true;

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS mcp_decisions (
                request_id TEXT PRIMARY KEY,
                request_payload JSONB NOT NULL,
                response_payload JSONB NOT NULL,
                verdict TEXT NOT NULL,
                score DOUBLE PRECISION NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_mcp_decisions_created_at
                ON mcp_decisions (created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_mcp_decisions_verdict
                ON mcp_decisions (verdict, created_at DESC);

            CREATE TABLE IF NOT EXISTS mcp_feedback (
                feedback_id TEXT PRIMARY KEY,
                request_id TEXT NOT NULL REFERENCES mcp_decisions(request_id) ON DELETE CASCADE,
                correct_verdict TEXT NOT NULL,
                notes TEXT,
                reporter TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_mcp_feedback_request_id
                ON mcp_feedback (request_id);
            CREATE INDEX IF NOT EXISTS idx_mcp_feedback_created_at
                ON mcp_feedback (created_at DESC);
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordDecisionAsync(ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO mcp_decisions
                (request_id, request_payload, response_payload, verdict, score, created_at)
            VALUES ($1, $2, $3, $4, $5, NOW())
            ON CONFLICT (request_id) DO UPDATE SET
                request_payload = EXCLUDED.request_payload,
                response_payload = EXCLUDED.response_payload,
                verdict = EXCLUDED.verdict,
                score = EXCLUDED.score,
                created_at = EXCLUDED.created_at
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = response.RequestId });
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(request, McpJson.Options), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new NpgsqlParameter { Value = JsonSerializer.Serialize(response, McpJson.Options), NpgsqlDbType = NpgsqlDbType.Jsonb });
        command.Parameters.Add(new NpgsqlParameter { Value = response.Verdict.ToString().ToLowerInvariant() });
        command.Parameters.Add(new NpgsqlParameter { Value = response.Score });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersistedDecision?> GetDecisionAsync(string requestId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT request_payload, response_payload FROM mcp_decisions WHERE request_id = $1";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = requestId });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var requestJson = reader.GetString(0);
        var responseJson = reader.GetString(1);
        var request = JsonSerializer.Deserialize<ClassifyRequest>(requestJson, McpJson.Options)!;
        var response = JsonSerializer.Deserialize<ClassifyResponse>(responseJson, McpJson.Options)!;
        return new PersistedDecision(request, response);
    }

    public async Task<string> RecordFeedbackAsync(string requestId, string correctVerdict, string? notes, string? reporter, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (var existsCommand = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM mcp_decisions WHERE request_id = $1)", connection))
        {
            existsCommand.Parameters.Add(new NpgsqlParameter { Value = requestId });
            var exists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            if (!exists)
            {
                throw new InvalidOperationException("classification request id was not found");
            }
        }

        var feedbackId = Guid.NewGuid().ToString();
        const string insertSql = """
            INSERT INTO mcp_feedback (feedback_id, request_id, correct_verdict, notes, reporter)
            VALUES ($1, $2, $3, $4, $5)
            """;
        await using var insertCommand = new NpgsqlCommand(insertSql, connection);
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = feedbackId });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = requestId });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = correctVerdict });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = (object?)notes ?? DBNull.Value });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = (object?)reporter ?? DBNull.Value });
        await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return feedbackId;
    }

    public async Task<DriftSummary> DriftSummaryAsync(DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        const string aggregateSql = """
            SELECT COUNT(*)::BIGINT AS samples,
                   COUNT(*) FILTER (WHERE created_at < $2)::BIGINT AS previous_samples,
                   COUNT(*) FILTER (WHERE created_at >= $2)::BIGINT AS current_samples,
                   COALESCE(AVG(score) FILTER (WHERE created_at >= $2), 0)::DOUBLE PRECISION AS score_mean,
                   COALESCE(STDDEV_POP(score), 0)::DOUBLE PRECISION AS score_stddev,
                   COALESCE(AVG(score) FILTER (WHERE created_at < $2), 0)::DOUBLE PRECISION AS previous_score_mean
            FROM mcp_decisions
            WHERE created_at >= $1
            """;
        ulong samples, previousSamplesRaw, currentSamplesRaw;
        double scoreMean, scoreStddev, previousScoreMean;
        await using (var command = new NpgsqlCommand(aggregateSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = midpoint.UtcDateTime });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            samples = (ulong)reader.GetInt64(0);
            previousSamplesRaw = (ulong)reader.GetInt64(1);
            currentSamplesRaw = (ulong)reader.GetInt64(2);
            scoreMean = reader.GetDouble(3);
            scoreStddev = reader.GetDouble(4);
            previousScoreMean = reader.GetDouble(5);
        }

        var verdictDistribution = new Dictionary<string, ulong>(StringComparer.Ordinal);
        const string verdictSql = """
            SELECT verdict, COUNT(*)::BIGINT AS count
            FROM mcp_decisions WHERE created_at >= $1 GROUP BY verdict
            """;
        await using (var command = new NpgsqlCommand(verdictSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                verdictDistribution[reader.GetString(0)] = (ulong)reader.GetInt64(1);
            }
        }

        var signalDrift = new Dictionary<string, double>(StringComparer.Ordinal);
        const string signalSql = """
            SELECT signal->>'name' AS name,
                   COUNT(*) FILTER (WHERE d.created_at < $2)::BIGINT AS previous_count,
                   COUNT(*) FILTER (WHERE d.created_at >= $2)::BIGINT AS current_count
            FROM mcp_decisions d
            CROSS JOIN LATERAL jsonb_array_elements(d.response_payload->'signals') signal
            WHERE d.created_at >= $1
            GROUP BY signal->>'name'
            """;
        var previousSamples = (double)previousSamplesRaw;
        var currentSamples = (double)currentSamplesRaw;
        await using (var command = new NpgsqlCommand(signalSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = midpoint.UtcDateTime });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.GetString(0);
                var previous = (double)reader.GetInt64(1);
                var current = (double)reader.GetInt64(2);
                var previousRate = previous / Math.Max(previousSamples, 1.0);
                var currentRate = current / Math.Max(currentSamples, 1.0);
                signalDrift[name] = currentRate - previousRate;
            }
        }

        return new DriftSummary(samples, previousSamplesRaw, currentSamplesRaw, scoreMean, scoreStddev, previousScoreMean, verdictDistribution, signalDrift);
    }

    public async Task<CalibrationSummary> CalibrationSummaryAsync(DateTimeOffset start, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT COUNT(*)::BIGINT AS samples,
                COUNT(*) FILTER (
                    WHERE d.verdict IN ('block', 'flag', 'challenge')
                      AND f.correct_verdict IN ('block', 'flag', 'challenge')
                )::BIGINT AS true_positives,
                COUNT(*) FILTER (
                    WHERE d.verdict IN ('block', 'flag', 'challenge')
                      AND f.correct_verdict = 'allow'
                )::BIGINT AS false_positives,
                COUNT(*) FILTER (
                    WHERE d.verdict = 'allow' AND f.correct_verdict = 'allow'
                )::BIGINT AS true_negatives,
                COUNT(*) FILTER (
                    WHERE d.verdict = 'allow'
                      AND f.correct_verdict IN ('block', 'flag', 'challenge')
                )::BIGINT AS false_negatives
            FROM (
                SELECT DISTINCT ON (request_id)
                       request_id, correct_verdict, created_at
                FROM mcp_feedback
                WHERE created_at >= $1
                ORDER BY request_id, created_at DESC
            ) f
            JOIN mcp_decisions d ON d.request_id = f.request_id
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new CalibrationSummary(
            (ulong)reader.GetInt64(0),
            (ulong)reader.GetInt64(1),
            (ulong)reader.GetInt64(2),
            (ulong)reader.GetInt64(3),
            (ulong)reader.GetInt64(4));
    }

    public async ValueTask DisposeAsync() => await _dataSource.DisposeAsync().ConfigureAwait(false);
}

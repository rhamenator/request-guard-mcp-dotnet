using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private const long SchemaInitLockKey = 0x5251_474d_4350_5343;
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
        string[] statements =
        [
            """
            CREATE TABLE IF NOT EXISTS mcp_decisions (
                request_id TEXT PRIMARY KEY,
                caller_scope TEXT,
                request_payload JSONB NOT NULL,
                response_payload JSONB NOT NULL,
                verdict TEXT NOT NULL,
                score DOUBLE PRECISION NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_mcp_decisions_created_at ON mcp_decisions (created_at DESC)",
            "CREATE INDEX IF NOT EXISTS idx_mcp_decisions_verdict ON mcp_decisions (verdict, created_at DESC)",
            """
            CREATE TABLE IF NOT EXISTS mcp_feedback (
                feedback_id TEXT PRIMARY KEY,
                request_id TEXT NOT NULL REFERENCES mcp_decisions(request_id) ON DELETE CASCADE,
                caller_scope TEXT,
                correct_verdict TEXT NOT NULL,
                notes TEXT,
                reporter TEXT,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_mcp_feedback_request_id ON mcp_feedback (request_id)",
            "CREATE INDEX IF NOT EXISTS idx_mcp_feedback_created_at ON mcp_feedback (created_at DESC)",
            // Retain legacy unscoped rows for offline migration, but never expose
            // them through authenticated replay, feedback, or reports.
            "ALTER TABLE mcp_decisions ADD COLUMN IF NOT EXISTS caller_scope TEXT",
            "ALTER TABLE mcp_feedback ADD COLUMN IF NOT EXISTS caller_scope TEXT",
            "CREATE INDEX IF NOT EXISTS idx_mcp_decisions_caller_created ON mcp_decisions (caller_scope, created_at DESC)",
            "CREATE INDEX IF NOT EXISTS idx_mcp_feedback_caller_created ON mcp_feedback (caller_scope, created_at DESC)",
            """
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conrelid = 'mcp_feedback'::regclass
                      AND conname = 'mcp_feedback_request_id_fkey'
                ) THEN
                    ALTER TABLE mcp_feedback
                        ADD CONSTRAINT mcp_feedback_request_id_fkey
                        FOREIGN KEY (request_id) REFERENCES mcp_decisions(request_id)
                        ON DELETE CASCADE;
                END IF;
            END $$
            """,
        ];
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var timeoutCommand = new NpgsqlCommand("SET LOCAL lock_timeout = '30s'", connection, transaction))
        {
            await timeoutCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter { Value = SchemaInitLockKey });
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        const string schemaCompleteSql = """
            SELECT to_regclass('mcp_decisions') IS NOT NULL
               AND to_regclass('mcp_feedback') IS NOT NULL
               AND to_regclass('idx_mcp_decisions_created_at') IS NOT NULL
               AND to_regclass('idx_mcp_decisions_verdict') IS NOT NULL
               AND to_regclass('idx_mcp_feedback_request_id') IS NOT NULL
               AND to_regclass('idx_mcp_feedback_created_at') IS NOT NULL
               AND to_regclass('idx_mcp_decisions_caller_created') IS NOT NULL
               AND to_regclass('idx_mcp_feedback_caller_created') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM pg_attribute
                   WHERE attrelid = to_regclass('mcp_decisions')
                     AND attname = 'caller_scope' AND NOT attisdropped
               )
               AND EXISTS (
                   SELECT 1 FROM pg_attribute
                   WHERE attrelid = to_regclass('mcp_feedback')
                     AND attname = 'caller_scope' AND NOT attisdropped
               )
               AND EXISTS (
                   SELECT 1 FROM pg_constraint
                   WHERE conrelid = to_regclass('mcp_feedback')
                     AND conname = 'mcp_feedback_request_id_fkey'
               )
            """;
        await using (var inspectCommand = new NpgsqlCommand(schemaCompleteSql, connection, transaction))
        {
            if ((bool)(await inspectCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        foreach (var statement in statements)
        {
            try
            {
                await using var command = new NpgsqlCommand(statement, connection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"PostgreSQL schema statement failed: {statement}", error);
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordDecisionAsync(string callerScope, ClassifyRequest request, ClassifyResponse response, CancellationToken cancellationToken = default)
    {
        var decisionKey = ScopedDecisionKey(callerScope, response.RequestId);
        var requestPayload = JsonSerializer.Serialize(ReplayFeatureSnapshot(request), McpJson.Options);
        var responsePayload = JsonSerializer.Serialize(response, McpJson.Options);
        const string sql = """
            INSERT INTO mcp_decisions
                (request_id, caller_scope, request_payload, response_payload, verdict, score, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, NOW())
            ON CONFLICT (request_id) DO UPDATE SET
                caller_scope = EXCLUDED.caller_scope,
                request_payload = EXCLUDED.request_payload,
                response_payload = EXCLUDED.response_payload,
                verdict = EXCLUDED.verdict,
                score = EXCLUDED.score,
                created_at = EXCLUDED.created_at
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        {
            lockCommand.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        string? oldRequest = null;
        string? oldResponse = null;
        await using (var existing = new NpgsqlCommand("SELECT request_payload, response_payload FROM mcp_decisions WHERE request_id = $1 AND caller_scope = $2 FOR UPDATE", connection, transaction))
        {
            existing.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
            existing.Parameters.Add(new NpgsqlParameter { Value = callerScope });
            await using var reader = await existing.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                oldRequest = reader.GetString(0);
                oldResponse = reader.GetString(1);
            }
        }

        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
            command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
            command.Parameters.Add(new NpgsqlParameter { Value = requestPayload, NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter { Value = responsePayload, NpgsqlDbType = NpgsqlDbType.Jsonb });
            command.Parameters.Add(new NpgsqlParameter { Value = response.Verdict.ToString().ToLowerInvariant() });
            command.Parameters.Add(new NpgsqlParameter { Value = response.Score });
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (oldRequest is not null && (!JsonEquivalent(oldRequest, requestPayload) || !ResponsesMatch(oldResponse!, responsePayload)))
        {
            await using var clear = new NpgsqlCommand("DELETE FROM mcp_feedback WHERE request_id = $1 AND caller_scope = $2", connection, transaction);
            clear.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
            clear.Parameters.Add(new NpgsqlParameter { Value = callerScope });
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PersistedDecision?> GetDecisionAsync(string callerScope, string requestId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT request_payload, response_payload FROM mcp_decisions WHERE request_id = $1 AND caller_scope = $2";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = ScopedDecisionKey(callerScope, requestId) });
        command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var requestJson = reader.GetString(0);
        var responseJson = reader.GetString(1);
        var request = JsonSerializer.Deserialize<ClassifyRequest>(requestJson, McpJson.Options)!;
        var response = JsonSerializer.Deserialize<ClassifyResponse>(responseJson, McpJson.Options)!;
        // sqlx's serde_json Value -> typed f64 path rounds JSONB classification scores to 15
        // fractional digits. Preserve that observable replay wire behavior across languages.
        response = response with { Score = Math.Round(response.Score, 15, MidpointRounding.ToEven) };
        return new PersistedDecision(request, response, requestJson, responseJson);
    }

    public async Task<string?> RecordFeedbackAsync(string callerScope, string requestId, string correctVerdict, string? notes, string? reporter, PersistedDecision expected, CancellationToken cancellationToken = default)
    {
        var decisionKey = ScopedDecisionKey(callerScope, requestId);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var currentCommand = new NpgsqlCommand("SELECT request_payload, response_payload FROM mcp_decisions WHERE request_id = $1 AND caller_scope = $2 FOR UPDATE", connection, transaction))
        {
            currentCommand.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
            currentCommand.Parameters.Add(new NpgsqlParameter { Value = callerScope });
            await using var reader = await currentCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ||
                !JsonEquivalent(reader.GetString(0), expected.RequestPayload) ||
                !ResponsesMatch(reader.GetString(1), expected.ResponsePayload))
            {
                return null;
            }
        }

        var feedbackId = Guid.NewGuid().ToString();
        const string insertSql = """
            INSERT INTO mcp_feedback (feedback_id, request_id, caller_scope, correct_verdict, notes, reporter)
            VALUES ($1, $2, $3, $4, $5, $6)
            """;
        await using var insertCommand = new NpgsqlCommand(insertSql, connection, transaction);
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = feedbackId });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = decisionKey });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = callerScope });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = correctVerdict });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = (object?)notes ?? DBNull.Value });
        insertCommand.Parameters.Add(new NpgsqlParameter { Value = (object?)reporter ?? DBNull.Value });
        await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return feedbackId;
    }

    public async Task<DriftSummary> DriftSummaryAsync(string callerScope, DateTimeOffset start, DateTimeOffset midpoint, CancellationToken cancellationToken = default)
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
            WHERE created_at >= $1 AND caller_scope = $3
            """;
        ulong samples, previousSamplesRaw, currentSamplesRaw;
        double scoreMean, scoreStddev, previousScoreMean;
        await using (var command = new NpgsqlCommand(aggregateSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = midpoint.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
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
            FROM mcp_decisions WHERE created_at >= $1 AND caller_scope = $2 GROUP BY verdict
            """;
        await using (var command = new NpgsqlCommand(verdictSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
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
            WHERE d.created_at >= $1 AND d.caller_scope = $3
            GROUP BY signal->>'name'
            """;
        var previousSamples = (double)previousSamplesRaw;
        var currentSamples = (double)currentSamplesRaw;
        await using (var command = new NpgsqlCommand(signalSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = midpoint.UtcDateTime });
            command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
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

    public async Task<CalibrationSummary> CalibrationSummaryAsync(string callerScope, DateTimeOffset start, CancellationToken cancellationToken = default)
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
                WHERE created_at >= $1 AND caller_scope = $2
                ORDER BY request_id, created_at DESC
            ) f
            JOIN mcp_decisions d ON d.request_id = f.request_id AND d.caller_scope = $2
            """;
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter { Value = start.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter { Value = callerScope });
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

    private static string ScopedDecisionKey(string callerScope, string requestId)
    {
        var scope = Encoding.UTF8.GetBytes(callerScope);
        var id = Encoding.UTF8.GetBytes(requestId);
        var input = new byte[scope.Length + id.Length + 1];
        scope.CopyTo(input, 0);
        id.CopyTo(input, scope.Length + 1);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static ClassifyRequest ReplayFeatureSnapshot(ClassifyRequest request)
    {
        Dictionary<string, string>? headers = null;
        if (request.Headers is not null)
        {
            headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in request.Headers.Keys)
            {
                var normalized = name.ToLowerInvariant();
                if (normalized is "accept" or "accept-language")
                {
                    headers[normalized] = string.Empty;
                }
            }
        }

        var path = request.Path;
        if (path is not null)
        {
            var delimiter = path.IndexOfAny(['?', '#']);
            if (delimiter >= 0)
            {
                path = path[..delimiter];
            }
        }

        return new ClassifyRequest
        {
            UserAgent = request.UserAgent,
            Path = path,
            Method = request.Method,
            Headers = headers,
        };
    }

    private static bool JsonEquivalent(string first, string second) =>
        JsonNode.DeepEquals(JsonNode.Parse(first), JsonNode.Parse(second));

    private static bool ResponsesMatch(string oldPayload, string newPayload)
    {
        var oldResponse = JsonNode.Parse(oldPayload) as JsonObject;
        var newResponse = JsonNode.Parse(newPayload) as JsonObject;
        if (oldResponse is null || newResponse is null ||
            oldResponse["score"]?.GetValue<double>() is not double oldScore ||
            newResponse["score"]?.GetValue<double>() is not double newScore ||
            Math.Abs(oldScore - newScore) > 1e-12)
        {
            return false;
        }

        oldResponse.Remove("score");
        oldResponse.Remove("latency_ms");
        newResponse.Remove("score");
        newResponse.Remove("latency_ms");
        return JsonNode.DeepEquals(oldResponse, newResponse);
    }
}

using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Core.Health;

/// <summary>
/// Computes the <c>health</c> response. Lives in Core (rather than the Tools project) because it
/// is used both by the unauthenticated <c>GET /health</c> endpoint (RequestGuardMcp.Mcp) and by
/// the <c>health</c> MCP tool (RequestGuardMcp.Tools) — Core is the one project both can depend
/// on. Ports src/tools/health.rs.
/// </summary>
public static class HealthCheck
{
    private static readonly DateTimeOffset StartTime = DateTimeOffset.UtcNow;

    public static async Task<HealthResponse> RunAsync(AppState state, CancellationToken cancellationToken = default)
    {
        var uptime = (ulong)Math.Max(0, (DateTimeOffset.UtcNow - StartTime).TotalSeconds);

        var checks = new Dictionary<string, ComponentHealth>
        {
            ["server"] = new ComponentHealth("healthy", Message: null, LatencyMs: 0),
            ["cache"] = new ComponentHealth("healthy", Message: null, LatencyMs: null),
        };

        var redisConfigured = state.Config.Redis.Url is not null;
        var redisStart = System.Diagnostics.Stopwatch.StartNew();
        var redisHealthy = !redisConfigured || await state.Redis.PingAsync(cancellationToken).ConfigureAwait(false);
        checks["redis"] = new ComponentHealth(redisConfigured ? redisHealthy ? "healthy" : "unhealthy" : "disabled",
            redisHealthy ? null : "configured Redis did not respond to PING", redisConfigured ? (ulong)redisStart.ElapsedMilliseconds : null);

        var postgresConfigured = state.Config.Postgres.Url is not null;
        var postgresStart = System.Diagnostics.Stopwatch.StartNew();
        var postgresHealthy = !postgresConfigured || await state.Postgres.PingAsync(cancellationToken).ConfigureAwait(false);
        checks["postgres"] = new ComponentHealth(postgresConfigured ? postgresHealthy ? "healthy" : "unhealthy" : "disabled",
            postgresHealthy ? null : "configured PostgreSQL did not respond to a health query", postgresConfigured ? (ulong)postgresStart.ElapsedMilliseconds : null);

        var geoipConfigured = state.Config.Geoip.MmdbPath is not null || state.Config.Geoip.CityMmdbPath is not null ||
                              state.Config.Geoip.AsnMmdbPath is not null || state.Config.Geoip.AnonymousIpMmdbPath is not null;
        var geoipHealthy = !geoipConfigured || state.Geoip.IsAvailable;
        checks["geoip"] = new ComponentHealth(geoipConfigured ? geoipHealthy ? "healthy" : "unhealthy" : "disabled",
            geoipHealthy ? null : "configured MaxMind databases are unavailable", null);

        return new HealthResponse(
            Status: redisHealthy && postgresHealthy && geoipHealthy ? "healthy" : "unhealthy",
            Version: state.BuildInfo.Version,
            UptimeSeconds: uptime,
            Checks: checks);
    }
}

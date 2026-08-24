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

    /// <summary>
    /// Redis/PostgreSQL/GeoIP component checks are added in Phase 4 (src/tools/health.rs's
    /// equivalent checks), once those integrations exist.
    /// </summary>
    public static HealthResponse Run(AppState state)
    {
        var uptime = (ulong)Math.Max(0, (DateTimeOffset.UtcNow - StartTime).TotalSeconds);

        var checks = new Dictionary<string, ComponentHealth>
        {
            ["server"] = new ComponentHealth("healthy", Message: null, LatencyMs: 0),
            ["cache"] = new ComponentHealth("healthy", Message: null, LatencyMs: null),
        };

        return new HealthResponse(
            Status: "healthy",
            Version: state.BuildInfo.Version,
            UptimeSeconds: uptime,
            Checks: checks);
    }
}

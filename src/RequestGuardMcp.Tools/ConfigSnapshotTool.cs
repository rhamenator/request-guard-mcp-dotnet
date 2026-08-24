using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>
/// The <c>config_snapshot</c> MCP tool. Ports src/tools/config_snapshot.rs. Only reports the
/// config sections that exist so far (host/port/log_level/limits/auth); redis/postgres/geoip/
/// telemetry/tls_fingerprints sections are added here as the phases that introduce them land.
/// <paramref name="redactSecrets"/>-equivalent behavior is currently a no-op since nothing in
/// today's snapshot is secret — it starts redacting once those later sections are added.
/// </summary>
public sealed class ConfigSnapshotTool : IMcpTool
{
    public string Name => "config_snapshot";

    public string Description => "Running config snapshot";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        _ = McpJson.ParamsOrDefault<ConfigSnapshotRequest>(parameters);
        var cfg = state.Config;

        var snapshot = new JsonObject
        {
            ["host"] = cfg.Host,
            ["port"] = cfg.Port,
            ["log_level"] = cfg.LogLevel,
            ["limits"] = new JsonObject
            {
                ["max_request_bytes"] = cfg.Limits.MaxRequestBytes,
                ["max_batch_size"] = cfg.Limits.MaxBatchSize,
                ["global_concurrency"] = cfg.Limits.GlobalConcurrency,
                ["per_tool_timeout_secs"] = cfg.Limits.PerToolTimeoutSecs,
                ["classify_timeout_secs"] = cfg.Limits.ClassifyTimeoutSecs,
            },
            ["auth"] = new JsonObject
            {
                ["enabled"] = cfg.Auth.Enabled,
                ["token_count"] = cfg.Auth.Tokens.Count,
            },
        };

        var response = new ConfigSnapshotResponse(snapshot, TimeUtil.NowRfc3339());
        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}

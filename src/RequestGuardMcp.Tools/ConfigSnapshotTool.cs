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
/// The <c>config_snapshot</c> MCP tool. Reports the running configuration without bearer tokens,
/// connection strings, or attestation keys.
/// </summary>
public sealed class ConfigSnapshotTool : IMcpTool
{
    public string Name => "config_snapshot";

    public string Description => "Running config snapshot";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<ConfigSnapshotRequest>(parameters);
        var redact = request.RedactSecrets ?? true;
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
            ["features"] = new JsonObject
            {
                ["enable_batch"] = cfg.Features.EnableBatch,
                ["enable_enrichment"] = cfg.Features.EnableEnrichment,
                ["enable_feedback"] = cfg.Features.EnableFeedback,
            },
            ["telemetry"] = new JsonObject
            {
                ["service_name"] = cfg.Telemetry.ServiceName,
                ["metrics_path"] = cfg.Telemetry.MetricsPath,
                // Rust deliberately hides whether an endpoint is configured when redaction is
                // requested, so even an absent value is represented by the placeholder.
                ["otlp_endpoint"] = redact ? JsonRedaction.RedactedPlaceholder : cfg.Telemetry.OtlpEndpoint,
            },
            ["redis"] = new JsonObject
            {
                ["configured"] = cfg.Redis.Url is not null,
                ["pool_size"] = cfg.Redis.PoolSize,
                ["key_prefix"] = cfg.Redis.KeyPrefix,
                ["cache_ttl_secs"] = cfg.Redis.CacheTtlSecs,
            },
            ["postgres"] = new JsonObject
            {
                ["configured"] = cfg.Postgres.Url is not null,
                ["max_connections"] = cfg.Postgres.MaxConnections,
                ["connect_timeout_secs"] = cfg.Postgres.ConnectTimeoutSecs,
            },
            ["geoip"] = new JsonObject
            {
                ["configured"] = cfg.Geoip.MmdbPath is not null || cfg.Geoip.CityMmdbPath is not null || cfg.Geoip.AsnMmdbPath is not null || cfg.Geoip.AnonymousIpMmdbPath is not null,
                ["city_configured"] = cfg.Geoip.MmdbPath is not null || cfg.Geoip.CityMmdbPath is not null,
                ["asn_configured"] = cfg.Geoip.AsnMmdbPath is not null,
                ["anonymous_ip_configured"] = cfg.Geoip.AnonymousIpMmdbPath is not null,
            },
            ["tls_fingerprints"] = new JsonObject
            {
                ["attestation_key_configured"] = cfg.TlsFingerprints.AttestationKey is not null,
                ["previous_attestation_key_configured"] = cfg.TlsFingerprints.PreviousAttestationKey is not null,
                ["max_age_seconds"] = cfg.TlsFingerprints.MaxAgeSeconds,
                ["known_bad_ja3_count"] = cfg.TlsFingerprints.KnownBadJa3.Count,
                ["known_bad_ja4_count"] = cfg.TlsFingerprints.KnownBadJa4.Count,
            },
        };

        var response = new ConfigSnapshotResponse(snapshot, TimeUtil.NowRfc3339());
        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}

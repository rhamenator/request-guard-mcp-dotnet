using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>
/// The <c>model_info</c> MCP tool. Reports all registered contracts and truthful feature/backend
/// availability, matching src/tools/model_info.rs.
/// </summary>
public sealed class ModelInfoTool(ToolRegistry registry) : IMcpTool
{
    private sealed record Contract(string Name, string Description);

    // Preserve the Rust response order and descriptions. Registry iteration order is an
    // implementation detail and previously made this endpoint observably different.
    private static readonly Contract[] Contracts =
    [
        new("classify", "Classify a request as bot/human"),
        new("explain", "Explain a classification decision"),
        new("batch_classify", "Classify multiple requests at once"),
        new("health", "Server health check"),
        new("model_info", "Return model and server metadata"),
        new("feedback", "Submit feedback on a classification"),
        new("score_breakdown", "Break down a classification score"),
        new("validate_payload", "Validate a tool payload against its schema"),
        new("feature_flags", "List or get feature flags"),
        new("warmup", "Warm up caches and engines"),
        new("replay_decision", "Replay a previous decision"),
        new("redact_preview", "Preview payload redaction"),
        new("enrich_ip", "Enrich an IP address with geo/ASN data"),
        new("enrich_asn", "Enrich an ASN with org data"),
        new("enrich_ua", "Enrich a user-agent string"),
        new("threat_lookup", "Look up a threat indicator"),
        new("canary_eval", "Evaluate a canary token"),
        new("abuse_pattern_match", "Match abuse patterns in text"),
        new("drift_report", "Report on score/signal drift"),
        new("calibration_report", "Precision/recall calibration report"),
        new("queue_status", "Status of processing queues"),
        new("config_snapshot", "Snapshot of current configuration"),
        new("self_test", "Run internal self-test suite"),
    ];

    public string Name => "model_info";

    public string Description => "Server and tool metadata";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var tools = Contracts
            .Where(contract => registry.Get(contract.Name) is not null)
            .Select(contract => new ToolInfo(contract.Name, contract.Description, Enabled: IsEnabled(state, contract.Name), Version: state.BuildInfo.Version))
            .ToList();

        var result = new ModelInfoResponse(
            ModelVersion: state.BuildInfo.Version,
            ToolCount: tools.Count,
            Tools: tools,
            BuildInfo: new BuildInfoResponse(
                state.BuildInfo.Version,
                state.BuildInfo.GitCommit,
                state.BuildInfo.BuildDate,
                state.BuildInfo.RuntimeVersion));

        return Task.FromResult(JsonSerializer.SerializeToNode(result, McpJson.Options));
    }

    private static bool IsEnabled(AppState state, string name) => name switch
    {
        "batch_classify" => state.Config.Features.EnableBatch,
        "feedback" => state.Config.Features.EnableFeedback && state.Postgres.IsAvailable,
        "replay_decision" or "drift_report" or "calibration_report" => state.Postgres.IsAvailable,
        "enrich_ip" => state.Config.Features.EnableEnrichment && (state.Geoip.HasIpDatabase || state.Reputation.IsConfigured),
        "enrich_asn" => state.Config.Features.EnableEnrichment && (state.Geoip.HasAsnDatabase || state.Reputation.IsConfigured),
        "enrich_ua" => state.Config.Features.EnableEnrichment,
        "threat_lookup" or "canary_eval" or "queue_status" => state.Redis.IsAvailable,
        _ => true,
    };
}

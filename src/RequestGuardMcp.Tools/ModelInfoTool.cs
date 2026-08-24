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
    public string Name => "model_info";

    public string Description => "Server and tool metadata";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var tools = registry.List()
            .Select(name => registry.Get(name))
            .Where(tool => tool is not null)
            .Select(tool => new ToolInfo(tool!.Name, tool.Description, Enabled: IsEnabled(state, tool.Name), Version: state.BuildInfo.Version))
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

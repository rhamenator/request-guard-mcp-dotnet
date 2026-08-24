using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>feature_flags</c> MCP tool. Ports src/tools/feature_flags.rs.</summary>
public sealed class FeatureFlagsTool : IMcpTool
{
    public string Name => "feature_flags";

    public string Description => "List feature flags";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<FeatureFlagsRequest>(parameters);

        Dictionary<string, bool> flags;
        if (request.Flag is { } name)
        {
            flags = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [name] = state.FeatureFlags.GetValueOrDefault(name, false),
            };
        }
        else
        {
            flags = new Dictionary<string, bool>(state.FeatureFlags, StringComparer.Ordinal);
        }

        var response = new FeatureFlagsResponse(flags);
        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}

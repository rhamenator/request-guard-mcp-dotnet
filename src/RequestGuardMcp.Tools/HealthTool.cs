using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Health;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>health</c> MCP tool. Ports src/mcp/tool_registry.rs's <c>HealthTool</c>.</summary>
public sealed class HealthTool : IMcpTool
{
    public string Name => "health";

    public string Description => "Server health check";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var result = HealthCheck.Run(state);
        return Task.FromResult(JsonSerializer.SerializeToNode(result, McpJson.Options));
    }
}

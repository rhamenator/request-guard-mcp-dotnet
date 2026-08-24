using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Classification;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>classify</c> MCP tool. Ports src/mcp/tool_registry.rs's <c>ClassifyTool</c>.</summary>
public sealed class ClassifyTool : IMcpTool
{
    public string Name => "classify";

    public string Description => "Classify a request as bot/human";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken) =>
        CallScopedAsync(state, parameters, "internal", cancellationToken);

    public async Task<JsonNode?> CallScopedAsync(AppState state, JsonNode? parameters, string callerScope, CancellationToken cancellationToken)
    {
        var request = McpJson.ParamsOrDefault<ClassifyRequest>(parameters);
        var result = await ClassifyService.RunAsync(state, request, callerScope, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(result, McpJson.Options);
    }
}

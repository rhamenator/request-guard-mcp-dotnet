using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>
/// The <c>model_info</c> MCP tool. Ports src/tools/model_info.rs. Unlike the Rust server (which
/// always lists its full, fixed set of 22 tools with an <c>enabled</c> flag reflecting
/// feature/backend availability), this port reports exactly the tools actually registered in the
/// <see cref="ToolRegistry"/> — honest about the port's in-progress tool surface rather than
/// listing tools that don't exist yet. See docs/architecture.md.
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
            .Select(tool => new ToolInfo(tool!.Name, tool.Description, Enabled: true, Version: state.BuildInfo.Version))
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
}

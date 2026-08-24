using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Protocol;

namespace RequestGuardMcp.Mcp.Registry;

/// <summary>Central registry mapping tool names to implementations. Ports src/mcp/tool_registry.rs's <c>ToolRegistry</c>.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IMcpTool> _tools = new(StringComparer.Ordinal);

    public void Register(IMcpTool tool) => _tools[tool.Name] = tool;

    public IMcpTool? Get(string name) => _tools.GetValueOrDefault(name);

    public IReadOnlyList<string> List() => [.. _tools.Keys.OrderBy(name => name, StringComparer.Ordinal)];

    public async Task<JsonNode?> DispatchAsync(
        AppState state,
        IncomingMcpMessage.Request request,
        string callerScope,
        CancellationToken cancellationToken)
    {
        var toolName = request.Method.StartsWith("tools/", StringComparison.Ordinal)
            ? request.Method["tools/".Length..]
            : request.Method;

        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw AppErrorException.ToolNotFound(toolName);
        }

        return await tool.CallScopedAsync(state, request.Params, callerScope, cancellationToken).ConfigureAwait(false);
    }
}

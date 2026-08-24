using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Mcp.Registry;

/// <summary>Every registered MCP tool implements this. Ports src/mcp/tool_registry.rs's <c>McpTool</c> trait.</summary>
public interface IMcpTool
{
    public string Name { get; }
    public string Description { get; }

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="CallAsync"/>, but told the authenticated caller's cache scope. Tools that
    /// don't need per-caller scoping (most of them) can ignore the default implementation below.
    /// </summary>
    public Task<JsonNode?> CallScopedAsync(AppState state, JsonNode? parameters, string callerScope, CancellationToken cancellationToken) =>
        CallAsync(state, parameters, cancellationToken);
}

using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>redact_preview</c> MCP tool. Ports src/tools/redact_preview.rs.</summary>
public sealed class RedactPreviewTool : IMcpTool
{
    private static readonly string[] DefaultSensitiveFields =
    [
        "authorization", "password", "token", "secret", "api_key", "cookie", "x-api-key", "x-auth-token",
    ];

    public string Name => "redact_preview";

    public string Description => "Preview field redaction";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<RedactPreviewRequest>(parameters);
        var payload = request.Payload?.DeepClone();
        var fields = request.Fields ?? [.. DefaultSensitiveFields];

        var fieldsRedacted = JsonRedaction.FindPresentFields(payload, fields);
        JsonRedaction.RedactFields(payload, fields);

        var response = new RedactPreviewResponse(payload, fieldsRedacted);
        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }
}

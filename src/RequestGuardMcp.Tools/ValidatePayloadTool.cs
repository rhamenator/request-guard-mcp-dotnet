using System.Text.Json;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Models.Request;
using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tools;

/// <summary>The <c>validate_payload</c> MCP tool. Ports src/tools/validate_payload.rs.</summary>
public sealed class ValidatePayloadTool : IMcpTool
{
    public string Name => "validate_payload";

    public string Description => "Validate a tool payload";

    public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var request = McpJson.RequiredParams<ValidatePayloadRequest>(parameters);
        var errors = ValidateForTool(request.Tool, request.Payload as JsonObject);
        var response = new ValidatePayloadResponse(errors.Count == 0, errors);
        return Task.FromResult(JsonSerializer.SerializeToNode(response, McpJson.Options));
    }

    private static List<ValidationError> ValidateForTool(string tool, JsonObject? payload)
    {
        var errors = new List<ValidationError>();
        var hasBatchItems = payload?["items"] is JsonArray { Count: > 0 };

        switch (tool)
        {
            case "classify":
                var hasIp = payload?.ContainsKey("ip") == true;
                var hasUserAgent = payload?.ContainsKey("user_agent") == true;
                var hasPath = payload?.ContainsKey("path") == true;
                if (!hasIp && !hasUserAgent && !hasPath)
                {
                    errors.Add(new ValidationError("/", "at least one of 'ip', 'user_agent', or 'path' is required"));
                }

                break;

            case "batch_classify" when hasBatchItems != true:
                errors.Add(new ValidationError("/items", "'items' must be a non-empty array"));
                break;

            case "feedback":
                foreach (var field in new[] { "request_id", "correct_verdict" })
                {
                    if (payload?.ContainsKey(field) != true)
                    {
                        errors.Add(new ValidationError($"/{field}", $"'{field}' is required"));
                    }
                }

                break;

            case "enrich_ip" when payload?.ContainsKey("ip") != true:
                errors.Add(new ValidationError("/ip", "'ip' is required"));
                break;
        }

        return errors;
    }
}

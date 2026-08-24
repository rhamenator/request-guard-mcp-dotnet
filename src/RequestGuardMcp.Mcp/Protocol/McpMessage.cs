using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Json;

namespace RequestGuardMcp.Mcp.Protocol;

/// <summary>
/// A parsed incoming message: either a call the server must dispatch, or something the server
/// silently ignores (a one-way notification, or a response echoed back by a client). Ports the
/// inbound half of src/mcp/protocol.rs's untagged <c>McpPayload</c> enum.
/// </summary>
public abstract record IncomingMcpMessage
{
    public sealed record Request(JsonNode? Id, string Method, JsonNode? Params) : IncomingMcpMessage;

    /// <summary>A notification (no <c>id</c>) or an inbound response — both are ignored.</summary>
    public sealed record Ignorable : IncomingMcpMessage;

    public sealed record ParseError : IncomingMcpMessage;
}

/// <summary>The JSON-RPC error payload embedded in a response. Ports src/mcp/protocol.rs's <c>McpError</c>.</summary>
public sealed record McpErrorPayload(int Code, string Message, JsonNode? Data = null);

/// <summary>
/// Parses and serializes the JSON-RPC 2.0 envelope used by the MCP transports. Ports
/// src/mcp/protocol.rs.
/// </summary>
public static class McpMessage
{
    public static IncomingMcpMessage Parse(string text)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return new IncomingMcpMessage.ParseError();
        }

        if (node is not JsonObject obj)
        {
            return new IncomingMcpMessage.ParseError();
        }

        var hasMethod = obj.TryGetPropertyValue("method", out var methodNode) && methodNode is not null;
        var hasId = obj.TryGetPropertyValue("id", out var idNode);

        if (!hasMethod)
        {
            // No "method": this is a response (or malformed). Either way, ignore it.
            return new IncomingMcpMessage.Ignorable();
        }

        var method = methodNode!.GetValue<string>();

        if (!hasId)
        {
            // A "method" with no "id" is a one-way notification: ignore it.
            return new IncomingMcpMessage.Ignorable();
        }

        obj.TryGetPropertyValue("params", out var paramsNode);
        return new IncomingMcpMessage.Request(idNode, method, paramsNode);
    }

    public static string Success(JsonNode? id, JsonNode? result) =>
        Serialize(id, result, error: null);

    public static string Error(JsonNode? id, int code, string message, JsonNode? data = null) =>
        Serialize(id, result: null, new McpErrorPayload(code, message, data));

    public static string ErrorFromApp(JsonNode? id, AppErrorException error) =>
        Error(id, -error.StatusCode, error.Code);

    private static string Serialize(JsonNode? id, JsonNode? result, McpErrorPayload? error)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["result"] = error is null ? result?.DeepClone() : null,
            ["error"] = error is null
                ? null
                : System.Text.Json.JsonSerializer.SerializeToNode(error, McpJson.Options),
        };

        return envelope.ToJsonString(McpJson.Options);
    }
}

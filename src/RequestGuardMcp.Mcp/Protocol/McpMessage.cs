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

    /// <summary>
    /// An invalid JSON-RPC request. A syntactically valid request identifier is retained when
    /// the remaining request shape is invalid so the error response can be correlated.
    /// </summary>
    public sealed record InvalidRequest(JsonNode? Id = null) : IncomingMcpMessage;
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
            using var document = System.Text.Json.JsonDocument.Parse(text);
            if (HasDuplicateProperties(document.RootElement))
            {
                return new IncomingMcpMessage.InvalidRequest();
            }

            node = JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return new IncomingMcpMessage.ParseError();
        }

        if (node is not JsonObject obj || !IsString(obj["jsonrpc"], "2.0"))
        {
            return new IncomingMcpMessage.InvalidRequest();
        }

        var hasMethod = obj.TryGetPropertyValue("method", out var methodNode);
        var hasId = obj.TryGetPropertyValue("id", out var idNode);
        var hasResult = obj.ContainsKey("result");
        var hasError = obj.ContainsKey("error");
        if (hasMethod)
        {
            if (methodNode is not JsonValue methodValue || !methodValue.TryGetValue<string>(out var method)
                || hasResult || hasError || (hasId && !IsId(idNode)))
            {
                return new IncomingMcpMessage.InvalidRequest();
            }

            if (obj.ContainsKey("params") && obj["params"] is not JsonObject)
            {
                return new IncomingMcpMessage.InvalidRequest(hasId ? idNode : null);
            }

            return hasId
                ? new IncomingMcpMessage.Request(idNode, method, obj["params"])
                : new IncomingMcpMessage.Ignorable();
        }

        // Accept only well-formed responses. This server issues no client requests, so
        // valid unsolicited responses are discarded without dispatch or side effects.
        if (!hasId || (!IsId(idNode) && !(idNode is null && hasError)) || hasResult == hasError
            || obj.ContainsKey("params"))
        {
            return new IncomingMcpMessage.InvalidRequest();
        }

        if (hasResult && obj["result"] is not JsonObject)
        {
            return new IncomingMcpMessage.InvalidRequest();
        }

        if (hasError && (obj["error"] is not JsonObject error || !IsInteger(error["code"])
            || error["message"] is not JsonValue message || !message.TryGetValue<string>(out _)))
        {
            return new IncomingMcpMessage.InvalidRequest();
        }

        return new IncomingMcpMessage.Ignorable();
    }

    private static bool IsString(JsonNode? node, string expected) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text == expected;

    private static bool IsId(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue<string>(out _) || IsInteger(node));

    private static bool IsInteger(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<System.Text.Json.JsonElement>(out var element)
            && element.ValueKind == System.Text.Json.JsonValueKind.Number
            && element.GetRawText().All(character => character is '-' or >= '0' and <= '9');

    private static bool HasDuplicateProperties(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperties);
        }

        return false;
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
        };
        if (error is null)
        {
            envelope["result"] = result?.DeepClone();
        }
        else
        {
            var payload = new JsonObject { ["code"] = error.Code, ["message"] = error.Message };
            if (error.Data is not null)
            {
                payload["data"] = error.Data.DeepClone();
            }

            envelope["error"] = payload;
        }

        return envelope.ToJsonString(McpJson.Options);
    }
}

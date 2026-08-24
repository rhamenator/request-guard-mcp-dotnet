using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Mcp.Protocol;

namespace RequestGuardMcp.Tests.Protocol;

public class McpMessageTests
{
    [Fact]
    public void ParsesRequestWithIdAndMethod()
    {
        var parsed = McpMessage.Parse("""{"jsonrpc":"2.0","id":1,"method":"health","params":{"a":1}}""");

        var request = Assert.IsType<IncomingMcpMessage.Request>(parsed);
        Assert.Equal("health", request.Method);
        Assert.Equal(1, request.Id!.GetValue<int>());
        Assert.NotNull(request.Params);
    }

    [Fact]
    public void NotificationWithNoIdIsIgnored()
    {
        var parsed = McpMessage.Parse("""{"jsonrpc":"2.0","method":"warmup","params":{}}""");

        Assert.IsType<IncomingMcpMessage.Ignorable>(parsed);
    }

    [Fact]
    public void ResponseWithNoMethodIsIgnored()
    {
        var parsed = McpMessage.Parse("""{"jsonrpc":"2.0","id":1,"result":{}}""");

        Assert.IsType<IncomingMcpMessage.Ignorable>(parsed);
    }

    [Fact]
    public void MalformedJsonIsAParseError()
    {
        var parsed = McpMessage.Parse("{not json");

        Assert.IsType<IncomingMcpMessage.ParseError>(parsed);
    }

    [Fact]
    public void NonObjectJsonIsAParseError()
    {
        var parsed = McpMessage.Parse("[1,2,3]");

        Assert.IsType<IncomingMcpMessage.ParseError>(parsed);
    }

    [Fact]
    public void SuccessSerializesJsonRpcEnvelope()
    {
        var json = McpMessage.Success(JsonValue.Create(1), JsonValue.Create("ok"));

        var node = JsonNode.Parse(json)!;
        Assert.Equal("2.0", node["jsonrpc"]!.GetValue<string>());
        Assert.Equal(1, node["id"]!.GetValue<int>());
        Assert.Equal("ok", node["result"]!.GetValue<string>());
        Assert.Null(node["error"]);
    }

    [Fact]
    public void ErrorFromAppUsesNegativeStatusCodeAndErrorCode()
    {
        var json = McpMessage.ErrorFromApp(JsonValue.Create(7), AppErrorException.ToolNotFound("nope"));

        var node = JsonNode.Parse(json)!;
        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.Null(node["result"]);
        Assert.Equal(-404, node["error"]!["code"]!.GetValue<int>());
        Assert.Equal("TOOL_NOT_FOUND", node["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public void NullIdIsPreservedAsRequest()
    {
        var parsed = McpMessage.Parse("""{"jsonrpc":"2.0","id":null,"method":"health"}""");

        var request = Assert.IsType<IncomingMcpMessage.Request>(parsed);
        Assert.Null(request.Id);
    }
}

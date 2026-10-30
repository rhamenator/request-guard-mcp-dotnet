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
    public void NonObjectJsonIsAnInvalidRequest()
    {
        var parsed = McpMessage.Parse("[1,2,3]");

        Assert.IsType<IncomingMcpMessage.InvalidRequest>(parsed);
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
        Assert.False(node.AsObject().ContainsKey("error"));
    }

    [Fact]
    public void ErrorFromAppUsesNegativeStatusCodeAndErrorCode()
    {
        var json = McpMessage.ErrorFromApp(JsonValue.Create(7), AppErrorException.ToolNotFound("nope"));

        var node = JsonNode.Parse(json)!;
        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.Null(node["result"]);
        Assert.False(node.AsObject().ContainsKey("result"));
        Assert.Equal(-404, node["error"]!["code"]!.GetValue<int>());
        Assert.Equal("TOOL_NOT_FOUND", node["error"]!["message"]!.GetValue<string>());
        Assert.False(node["error"]!.AsObject().ContainsKey("data"));
    }

    [Fact]
    public void NullRequestIdIsRejected()
    {
        var parsed = McpMessage.Parse("""{"jsonrpc":"2.0","id":null,"method":"health"}""");

        var invalid = Assert.IsType<IncomingMcpMessage.InvalidRequest>(parsed);
        Assert.Null(invalid.Id);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"request-1\"")]
    public void InvalidParamsPreserveAValidRequestId(string id)
    {
        var parsed = McpMessage.Parse("{\"jsonrpc\":\"2.0\",\"id\":" + id
            + ",\"method\":\"health\",\"params\":[]}");

        var invalid = Assert.IsType<IncomingMcpMessage.InvalidRequest>(parsed);
        Assert.Equal(id, invalid.Id!.ToJsonString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1.5")]
    public void InvalidRequestIdsAreNotReflected(string id)
    {
        var parsed = McpMessage.Parse("{\"jsonrpc\":\"2.0\",\"id\":" + id
            + ",\"method\":\"health\",\"params\":[]}");

        var invalid = Assert.IsType<IncomingMcpMessage.InvalidRequest>(parsed);
        Assert.Null(invalid.Id);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"id\":1,\"method\":\"health\"}")]
    [InlineData("{\"jsonrpc\":\"1.0\",\"id\":1,\"method\":\"health\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":true,\"method\":\"health\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1.5,\"method\":\"health\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":{},\"method\":\"health\"}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":42}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"health\",\"params\":[]}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"health\",\"params\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"health\",\"result\":{}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"error\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":1.5,\"message\":\"bad\"}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1,\"message\":42}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"id\":2,\"method\":\"health\"}")]
    public void RejectsInvalidEnvelopes(string json) =>
        Assert.IsType<IncomingMcpMessage.InvalidRequest>(McpMessage.Parse(json));

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-1,\"message\":\"bad\"}}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"bad\"}}")]
    public void ValidErrorResponsesAreIgnored(string json) =>
        Assert.IsType<IncomingMcpMessage.Ignorable>(McpMessage.Parse(json));

}

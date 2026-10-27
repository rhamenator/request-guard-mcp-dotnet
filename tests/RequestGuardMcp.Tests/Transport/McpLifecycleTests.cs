using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

public class McpLifecycleTests
{
    private static async Task<JsonObject> CallAsync(string method, JsonNode? parameters = null)
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        var dispatcher = new McpDispatcher(ToolRegistryFactory.Create(), NullLogger<McpDispatcher>.Instance);
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "test-id", ["method"] = method };
        if (parameters is not null)
        {
            request["params"] = parameters;
        }

        var response = await dispatcher.ProcessMessageAsync(request.ToJsonString(), state, "test-scope", CancellationToken.None);
        var envelope = JsonNode.Parse(response!)!.AsObject();
        Assert.Equal("test-id", envelope["id"]!.GetValue<string>());
        Assert.True(envelope.ContainsKey("result") ^ envelope.ContainsKey("error"));
        return envelope;
    }

    [Theory]
    [InlineData("2025-06-18")]
    [InlineData("2024-11-05")]
    public async Task InitializeOffersSupportedVersion(string version)
    {
        var response = await CallAsync("initialize", new JsonObject { ["protocolVersion"] = version });
        Assert.Equal("2025-06-18", response["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.False(response["result"]!["capabilities"]!["tools"]!["listChanged"]!.GetValue<bool>());
        Assert.Equal("request-guard-mcp-dotnet", response["result"]!["serverInfo"]!["name"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("initialize", "{}")]
    [InlineData("initialize", "{\"protocolVersion\":42}")]
    [InlineData("tools/call", "{}")]
    [InlineData("tools/call", "{\"name\":42}")]
    [InlineData("tools/call", "{\"name\":\"health\",\"arguments\":[]}")]
    [InlineData("tools/call", "{\"name\":\"health\",\"arguments\":null}")]
    public async Task LifecycleArgumentsAreValidated(string method, string json)
    {
        var response = await CallAsync(method, JsonNode.Parse(json));
        Assert.Equal(-32602, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task PingReturnsAnEmptyObject()
    {
        var response = await CallAsync("ping");
        Assert.Empty(response["result"]!.AsObject());
    }

    [Fact]
    public async Task ToolDefinitionsCoverExactlyTheRegistry()
    {
        var response = await CallAsync("tools/list");
        var tools = response["result"]!["tools"]!.AsArray();
        Assert.Equal(ToolRegistryFactory.Create().List(), tools.Select(tool => tool!["name"]!.GetValue<string>()));
        Assert.Equal(23, tools.Count);
        foreach (var tool in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool!["description"]!.GetValue<string>()));
            Assert.Equal("object", tool["inputSchema"]!["type"]!.GetValue<string>());
            Assert.NotNull(tool["inputSchema"]!["properties"]);
        }
    }

    [Fact]
    public async Task StandardCallWrapsActualClassifierResult()
    {
        var response = await CallAsync("tools/call", JsonNode.Parse("""{"name":"classify","arguments":{"user_agent":"GPTBot/1.0","request_id":"lifecycle-test"}}"""));
        var result = response["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        Assert.Equal("lifecycle-test", result["structuredContent"]!["request_id"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(result["structuredContent"], JsonNode.Parse(result["content"]![0]!["text"]!.GetValue<string>())));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("tools/health")]
    public async Task StandardCallUsesExactRegisteredName(string name)
    {
        var response = await CallAsync("tools/call", new JsonObject { ["name"] = name });
        Assert.Equal(-404, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task StandardCallPreservesToolFailure()
    {
        var response = await CallAsync("tools/call", new JsonObject { ["name"] = "explain", ["arguments"] = new JsonObject() });
        Assert.Equal(-400, response["error"]!["code"]!.GetValue<int>());
    }
}

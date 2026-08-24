using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

public class McpDispatcherTests
{
    private static (McpDispatcher Dispatcher, AppState State) NewDispatcher(AppConfig? config = null)
    {
        var registry = new ToolRegistry();
        registry.Register(new HealthTool());
        registry.Register(new ModelInfoTool(registry));

        var dispatcher = new McpDispatcher(registry, NullLogger<McpDispatcher>.Instance);
        var state = new AppState(config ?? new AppConfig { Auth = new AuthConfig { Enabled = false } });
        return (dispatcher, state);
    }

    [Fact]
    public async Task NotificationProducesNoResponse()
    {
        var (dispatcher, state) = NewDispatcher();

        var response = await dispatcher.ProcessMessageAsync(
            """{"jsonrpc":"2.0","method":"health"}""", state, "public", CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task UnknownToolReturnsToolNotFoundError()
    {
        var (dispatcher, state) = NewDispatcher();

        var response = await dispatcher.ProcessMessageAsync(
            """{"jsonrpc":"2.0","id":1,"method":"nope"}""", state, "public", CancellationToken.None);

        var node = JsonNode.Parse(response!)!;
        Assert.Equal(-404, node["error"]!["code"]!.GetValue<int>());
        Assert.Equal("TOOL_NOT_FOUND", node["error"]!["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task HealthToolDispatchesSuccessfully()
    {
        var (dispatcher, state) = NewDispatcher();

        var response = await dispatcher.ProcessMessageAsync(
            """{"jsonrpc":"2.0","id":1,"method":"health"}""", state, "public", CancellationToken.None);

        var node = JsonNode.Parse(response!)!;
        Assert.Equal("healthy", node["result"]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ToolsPrefixedMethodNameIsDispatched()
    {
        var (dispatcher, state) = NewDispatcher();

        var response = await dispatcher.ProcessMessageAsync(
            """{"jsonrpc":"2.0","id":1,"method":"tools/model_info"}""", state, "public", CancellationToken.None);

        var node = JsonNode.Parse(response!)!;
        Assert.Equal(2, node["result"]!["tool_count"]!.GetValue<int>());
    }

    [Fact]
    public async Task OversizedRequestIsRejected()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { MaxRequestBytes = 10 },
        };
        var (dispatcher, state) = NewDispatcher(config);

        var response = await dispatcher.ProcessMessageAsync(
            """{"jsonrpc":"2.0","id":1,"method":"health"}""", state, "public", CancellationToken.None);

        var node = JsonNode.Parse(response!)!;
        Assert.Equal(-413, node["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task MalformedJsonReturnsParseError()
    {
        var (dispatcher, state) = NewDispatcher();

        var response = await dispatcher.ProcessMessageAsync("not json", state, "public", CancellationToken.None);

        var node = JsonNode.Parse(response!)!;
        Assert.Equal(-32700, node["error"]!["code"]!.GetValue<int>());
    }
}

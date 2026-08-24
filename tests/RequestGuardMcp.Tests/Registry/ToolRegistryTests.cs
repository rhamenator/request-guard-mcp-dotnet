using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Protocol;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Tests.Registry;

public class ToolRegistryTests
{
    private sealed class EchoTool : IMcpTool
    {
        public string Name => "echo";
        public string Description => "echoes params back";

        public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken) =>
            Task.FromResult(parameters?.DeepClone());
    }

    private static AppState NewState() =>
        new(new AppConfig { Auth = new AuthConfig { Enabled = false } });

    private static IncomingMcpMessage.Request Req(string method, JsonNode? paramsNode = null) =>
        new(Id: JsonValue.Create(1), Method: method, Params: paramsNode);

    [Fact]
    public void ListReturnsRegisteredToolNamesSorted()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());

        Assert.Equal(["echo"], registry.List());
    }

    [Fact]
    public async Task DispatchStripsToolsPrefix()
    {
        var registry = new ToolRegistry();
        registry.Register(new EchoTool());

        var result = await registry.DispatchAsync(NewState(), Req("tools/echo", JsonValue.Create("hi")), "public", CancellationToken.None);

        Assert.Equal("hi", result!.GetValue<string>());
    }

    [Fact]
    public async Task DispatchThrowsToolNotFoundForUnknownTool()
    {
        var registry = new ToolRegistry();

        var error = await Assert.ThrowsAsync<AppErrorException>(() =>
            registry.DispatchAsync(NewState(), Req("nonexistent"), "public", CancellationToken.None));

        Assert.Equal(AppErrorKind.ToolNotFound, error.Kind);
    }
}

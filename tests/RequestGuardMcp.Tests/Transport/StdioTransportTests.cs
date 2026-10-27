using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

public class StdioTransportTests
{
    private const string Ping = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

    private static async Task<JsonNode[]> RunAsync(byte[] bytes, int maxBytes = 1024)
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false }, Limits = new LimitsConfig { MaxRequestBytes = maxBytes } });
        var dispatcher = new McpDispatcher(ToolRegistryFactory.Create(), NullLogger<McpDispatcher>.Instance);
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await StdioTransport.RunAsync(input, output, dispatcher, state, CancellationToken.None);
        return Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!).ToArray();
    }

    [Fact]
    public async Task NotificationsAreSilentAndCrLfRequestsRoundTrip()
    {
        var responses = await RunAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n" + Ping + "\r\n"));
        var response = Assert.Single(responses);
        Assert.Equal(1, response["id"]!.GetValue<int>());
        Assert.Empty(response["result"]!.AsObject());
    }

    [Theory]
    [InlineData("{", -32700)]
    [InlineData("{}", -32600)]
    public async Task BadMessageDoesNotConsumeFollowingRequest(string bad, int code)
    {
        var responses = await RunAsync(Encoding.UTF8.GetBytes(bad + "\n" + Ping + "\n"));
        Assert.Equal(2, responses.Length);
        Assert.Equal(code, responses[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(1, responses[1]["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task OversizedLineIsDrainedAndNextRequestSucceeds()
    {
        var responses = await RunAsync(Encoding.UTF8.GetBytes(new string('x', 20000) + "\n" + Ping + "\n"), 100);
        Assert.Equal(2, responses.Length);
        Assert.Equal(-413, responses[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(1, responses[1]["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task InvalidUtf8DoesNotConsumeFollowingRequest()
    {
        var responses = await RunAsync([0xff, (byte)'\n', .. Encoding.UTF8.GetBytes(Ping + "\n")]);
        Assert.Equal(2, responses.Length);
        Assert.Equal(-32700, responses[0]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(1, responses[1]["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task MissingFinalDelimiterIsReportedWithoutDispatch()
    {
        var response = Assert.Single(await RunAsync(Encoding.UTF8.GetBytes(Ping)));
        Assert.Equal(-32700, response["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task InputWaitIsCancellableEvenIfStreamIgnoresCancellation()
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        var dispatcher = new McpDispatcher(new ToolRegistry(), NullLogger<McpDispatcher>.Instance);
        using var input = new NeverReadingStream();
        using var output = new MemoryStream();
        using var cancel = new CancellationTokenSource();
        var running = StdioTransport.RunAsync(input, output, dispatcher, state, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task BlockedOutputIsCancellableEvenIfStreamIgnoresCancellation()
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        var dispatcher = new McpDispatcher(new ToolRegistry(), NullLogger<McpDispatcher>.Instance);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Ping + "\n"));
        using var output = new NeverWritingStream();
        using var cancel = new CancellationTokenSource();
        var running = StdioTransport.RunAsync(input, output, dispatcher, state, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task SeparateSubprocessTransportsHaveDistinctStableScopes()
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        var registry = new ToolRegistry();
        registry.Register(new ScopeTool());
        var dispatcher = new McpDispatcher(registry, NullLogger<McpDispatcher>.Instance);
        var request = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"scope\"}\n";
        var scopes = new List<string>();
        for (var index = 0; index < 2; index++)
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(request + request));
            using var output = new MemoryStream();
            await StdioTransport.RunAsync(input, output, dispatcher, state, CancellationToken.None);
            var responses = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var first = JsonNode.Parse(responses[0])!["result"]!["scope"]!.GetValue<string>();
            Assert.Equal(first, JsonNode.Parse(responses[1])!["result"]!["scope"]!.GetValue<string>());
            scopes.Add(first);
        }

        Assert.NotEqual(scopes[0], scopes[1]);
    }

    private sealed class ScopeTool : IMcpTool
    {
        public string Name => "scope";
        public string Description => "test";
        public Task<JsonNode?> CallAsync(AppState state, JsonNode? parameters, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<JsonNode?> CallScopedAsync(AppState state, JsonNode? parameters, string callerScope, CancellationToken cancellationToken) =>
            Task.FromResult<JsonNode?>(new JsonObject { ["scope"] = callerScope });
    }

    private sealed class NeverWritingStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task);
    }

    private sealed class NeverReadingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
    }
}

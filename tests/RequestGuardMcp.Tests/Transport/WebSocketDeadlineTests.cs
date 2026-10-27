using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Registry;
using RequestGuardMcp.Mcp.Transport;

namespace RequestGuardMcp.Tests.Transport;

public class WebSocketDeadlineTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("trickle")]
    [InlineData("write")]
    [InlineData("close")]
    public async Task CancellationIgnoringSocketCannotRetainHandler(string mode)
    {
        await using var state = new AppState(new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { WebSocketIdleTimeoutSecs = 1, WebSocketWriteTimeoutSecs = 1 },
        });
        var dispatcher = new McpDispatcher(new ToolRegistry(), NullLogger<McpDispatcher>.Instance);
        var handler = new WebSocketConnectionHandler(dispatcher, NullLogger<WebSocketConnectionHandler>.Instance);
        using var socket = new StalledSocket(mode);
        await handler.HandleAsync(socket, state, "test", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(WebSocketState.Aborted, socket.State);
        Assert.Contains("mcp_active_connections 0", state.Metrics.RenderPrometheus(), StringComparison.Ordinal);
        Assert.False(socket.FullCloseCalled, "close output must not wait for another peer close frame");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonpositiveWebSocketBudgetsFailConfiguration(bool write)
    {
        var limits = new LimitsConfig();
        if (write)
        {
            limits.WebSocketWriteTimeoutSecs = 0;
        }
        else
        {
            limits.WebSocketIdleTimeoutSecs = 0;
        }

        Assert.Throws<ConfigValidationException>(() => new AppConfig { Auth = new AuthConfig { Enabled = false }, Limits = limits }.Validate());
    }

    private sealed class StalledSocket(string mode) : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open;
        public bool FullCloseCalled { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            FullCloseCalled = true;
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (mode == "trickle")
            {
                return TrickleAsync(buffer);
            }

            if (mode == "close")
            {
                _state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
            }

            if (mode == "write")
            {
                var ping = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
                ping.AsSpan().CopyTo(buffer.AsSpan());
                return Task.FromResult(new WebSocketReceiveResult(ping.Length, WebSocketMessageType.Text, true));
            }

            return new TaskCompletionSource<WebSocketReceiveResult>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        private static async Task<WebSocketReceiveResult> TrickleAsync(ArraySegment<byte> buffer)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
            buffer[0] = (byte)'{';
            return new WebSocketReceiveResult(1, WebSocketMessageType.Text, false);
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }
}

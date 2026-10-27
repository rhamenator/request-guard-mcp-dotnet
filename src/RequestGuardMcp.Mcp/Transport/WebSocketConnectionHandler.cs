using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Mcp.Transport;

/// <summary>Handles a single WebSocket connection's lifecycle. Ports src/mcp/transport_ws.rs's <c>handle_ws_connection</c>.</summary>
public sealed class WebSocketConnectionHandler(McpDispatcher dispatcher, ILogger<WebSocketConnectionHandler> logger)
{
    private const int ReceiveBufferSize = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task HandleAsync(WebSocket socket, AppState state, string callerScope, CancellationToken cancellationToken)
    {
        logger.LogInformation("WebSocket connection established");
        state.Metrics.ConnectionOpened();

        var buffer = new byte[ReceiveBufferSize];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                using var messageDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                messageDeadline.CancelAfter(TimeSpan.FromSeconds(state.Config.Limits.WebSocketIdleTimeoutSecs));
                WebSocketReceiveResult result;
                do
                {
                    // One budget covers the whole message, including all fragments. A peer
                    // cannot retain a slot forever by trickling partial frames.
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), messageDeadline.Token)
                        .WaitAsync(messageDeadline.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (messageStream.Length + result.Count > state.Config.Limits.MaxRequestBytes)
                    {
                        await WriteBoundedAsync(token => socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, "request too large", token), state, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await WriteBoundedAsync(token => socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token), state, cancellationToken).ConfigureAwait(false);
                    break;
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(messageStream.ToArray());
                }
                catch (DecoderFallbackException)
                {
                    logger.LogWarning("received non-UTF-8 binary MCP message");
                    var parseError = Encoding.UTF8.GetBytes(Protocol.McpMessage.Error(null, -32700, "Parse error: MCP messages must be UTF-8"));
                    await WriteBoundedAsync(token => socket.SendAsync(new ArraySegment<byte>(parseError), WebSocketMessageType.Text, endOfMessage: true, token), state, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var response = await dispatcher.ProcessMessageAsync(text, state, callerScope, cancellationToken).ConfigureAwait(false);
                if (response is not null)
                {
                    var payload = Encoding.UTF8.GetBytes(response);
                    await WriteBoundedAsync(token => socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, token), state, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("WebSocket message or write deadline expired");
            }

            socket.Abort();
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "WebSocket error");
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseSent or WebSocketState.CloseReceived)
            {
                socket.Abort();
            }

            state.Metrics.ConnectionClosed();
            logger.LogInformation("WebSocket connection closed");
        }
    }
    private static async Task WriteBoundedAsync(Func<CancellationToken, Task> operation, AppState state, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(state.Config.Limits.WebSocketWriteTimeoutSecs));
        await operation(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
    }

}

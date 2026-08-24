using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Mcp.Transport;

/// <summary>Handles a single WebSocket connection's lifecycle. Ports src/mcp/transport_ws.rs's <c>handle_ws_connection</c>.</summary>
public sealed class WebSocketConnectionHandler(McpDispatcher dispatcher, ILogger<WebSocketConnectionHandler> logger)
{
    private const int ReceiveBufferSize = 16 * 1024;

    public async Task HandleAsync(WebSocket socket, AppState state, string callerScope, CancellationToken cancellationToken)
    {
        logger.LogInformation("WebSocket connection established");

        var buffer = new byte[ReceiveBufferSize];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken).ConfigureAwait(false);
                    break;
                }

                var text = Encoding.UTF8.GetString(messageStream.ToArray());
                var response = await dispatcher.ProcessMessageAsync(text, state, callerScope, cancellationToken).ConfigureAwait(false);
                if (response is not null)
                {
                    var payload = Encoding.UTF8.GetBytes(response);
                    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down or client disconnected; nothing to do.
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "WebSocket error");
        }
        finally
        {
            logger.LogInformation("WebSocket connection closed");
        }
    }
}

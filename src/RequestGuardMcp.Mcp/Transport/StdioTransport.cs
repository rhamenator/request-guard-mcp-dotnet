using System.Text;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Protocol;

namespace RequestGuardMcp.Mcp.Transport;

/// <summary>Process-local, bounded UTF-8 newline-delimited MCP transport. Does not open a listener.</summary>
public static class StdioTransport
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    public static async Task RunAsync(Stream input, Stream output, McpDispatcher dispatcher, AppState state,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var line = new MemoryStream();
        var oversized = false;
        // A process-local random identity prevents separate client subprocesses sharing a
        // Redis prefix from sharing cached decisions. There is no Bearer header on stdin.
        var callerScope = $"stdio:{Guid.NewGuid():N}";
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                if (line.Length > 0 || oversized)
                {
                    await WriteAsync(McpMessage.Error(null, -32700, "Parse error: unterminated stdio message"), output, cancellationToken).ConfigureAwait(false);
                }

                return;
            }

            for (var index = 0; index < count; index++)
            {
                var current = buffer[index];
                if (current != '\n')
                {
                    if (!oversized)
                    {
                        if (line.Length >= state.Config.Limits.MaxRequestBytes)
                        {
                            oversized = true;
                            line.SetLength(0);
                        }
                        else
                        {
                            line.WriteByte(current);
                        }
                    }

                    continue;
                }

                string? response;
                if (oversized)
                {
                    response = McpMessage.Error(null, -413, "REQUEST_TOO_LARGE");
                }
                else
                {
                    try
                    {
                        var length = checked((int)line.Length);
                        if (length > 0 && line.GetBuffer()[length - 1] == '\r')
                        {
                            length--;
                        }

                        var text = StrictUtf8.GetString(line.GetBuffer(), 0, length);
                        response = await dispatcher.ProcessMessageAsync(text, state, callerScope, cancellationToken).ConfigureAwait(false);
                    }
                    catch (DecoderFallbackException)
                    {
                        response = McpMessage.Error(null, -32700, "Parse error: MCP messages must be UTF-8");
                    }
                }

                line.SetLength(0);
                oversized = false;
                if (response is not null)
                {
                    await WriteAsync(response, output, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private static async Task WriteAsync(string response, Stream output, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(WriteTimeout);
        await output.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false);
        await output.FlushAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
    }
}

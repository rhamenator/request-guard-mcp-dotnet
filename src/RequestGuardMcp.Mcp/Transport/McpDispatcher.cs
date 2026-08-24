using System.Text.Json.Nodes;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Protocol;
using RequestGuardMcp.Mcp.Registry;

namespace RequestGuardMcp.Mcp.Transport;

/// <summary>
/// Parses one incoming MCP message, dispatches it through the <see cref="ToolRegistry"/> under
/// the global concurrency semaphore and a per-tool timeout, and serializes the response. Shared
/// by both the WebSocket and HTTP POST transports. Ports src/mcp/transport_ws.rs's
/// <c>process_message</c>.
/// </summary>
public sealed class McpDispatcher(ToolRegistry registry, ILogger<McpDispatcher> logger)
{
    private static readonly ActivitySource ActivitySource = new("RequestGuardMcp");
    /// <summary>Returns the JSON text to send back, or null when the message was a notification (no reply).</summary>
    public async Task<string?> ProcessMessageAsync(string text, AppState state, string callerScope, CancellationToken cancellationToken)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) > state.Config.Limits.MaxRequestBytes)
        {
            return McpMessage.Error(null, -413, AppErrorException.RequestTooLarge().Code);
        }

        IncomingMcpMessage parsed;
        try
        {
            parsed = McpMessage.Parse(text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "failed to parse MCP message");
            return McpMessage.Error(null, -32700, "Parse error");
        }

        if (parsed is IncomingMcpMessage.ParseError)
        {
            return McpMessage.Error(null, -32700, "Parse error");
        }

        if (parsed is not IncomingMcpMessage.Request request)
        {
            // Notifications and responses from the client are ignored.
            return null;
        }

        var toolName = request.Method.StartsWith("tools/", StringComparison.Ordinal)
            ? request.Method["tools/".Length..]
            : request.Method;
        var stopwatch = Stopwatch.StartNew();
        using var activity = ActivitySource.StartActivity("mcp.tool", ActivityKind.Internal);
        activity?.SetTag("mcp.tool.name", toolName);

        await state.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var toolTimeout = toolName == "classify" ? state.Config.ClassifyTimeout : state.Config.PerToolTimeout;
            using var timeoutCts = new CancellationTokenSource(toolTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            string? operationId = null;
            if (state.Redis.IsAvailable)
            {
                try
                {
                    operationId = await state.Redis.RecordToolStartedAsync(toolName, Math.Max(1, (long)toolTimeout.TotalSeconds), linkedCts.Token)
                        .WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    logger.LogWarning(error, "failed to record Redis operation start: {Tool}", toolName);
                }
            }

            JsonNode? result;
            try
            {
                result = await registry.DispatchAsync(state, request, callerScope, linkedCts.Token)
                    .WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (AppErrorException appError)
            {
                logger.LogWarning(appError, "tool error: {Tool}", toolName);
                state.Metrics.Record(toolName, "error", stopwatch.Elapsed.TotalSeconds, appError.Code);
                activity?.SetStatus(ActivityStatusCode.Error, appError.Code);
                return McpMessage.ErrorFromApp(request.Id, appError);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                logger.LogError("tool timeout: {Tool}", toolName);
                state.Metrics.Record(toolName, "timeout", stopwatch.Elapsed.TotalSeconds, "TIMEOUT");
                activity?.SetStatus(ActivityStatusCode.Error, "TIMEOUT");
                return McpMessage.ErrorFromApp(request.Id, AppErrorException.Timeout());
            }
            catch (Exception error)
            {
                logger.LogError(error, "unexpected tool failure: {Tool}", toolName);
                state.Metrics.Record(toolName, "error", stopwatch.Elapsed.TotalSeconds, "INTERNAL_ERROR");
                activity?.SetStatus(ActivityStatusCode.Error, "INTERNAL_ERROR");
                return McpMessage.ErrorFromApp(request.Id, AppErrorException.Internal());
            }
            finally
            {
                if (operationId is not null)
                {
                    try
                    {
                        await state.Redis.RecordToolFinishedAsync(toolName, operationId, CancellationToken.None)
                            .WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        logger.LogWarning(error, "failed to record Redis operation completion: {Tool}", toolName);
                    }
                }
            }

            state.Metrics.Record(toolName, "ok", stopwatch.Elapsed.TotalSeconds);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return McpMessage.Success(request.Id, result);
        }
        finally
        {
            state.Semaphore.Release();
        }
    }
}

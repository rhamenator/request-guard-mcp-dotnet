using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Core.Health;
using RequestGuardMcp.Core.Json;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp.Auth;
using RequestGuardMcp.Mcp.Transport;

namespace RequestGuardMcp.Mcp;

/// <summary>
/// Maps the MCP server's routes. The host must call <c>app.UseWebSockets()</c> before this.
/// Ports src/mcp/server.rs's <c>build_router</c>.
/// </summary>
public static class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpServer(this IEndpointRouteBuilder endpoints, string metricsPath = "/metrics")
    {
        endpoints.MapGet("/mcp", HandleWebSocketUpgradeAsync);
        endpoints.MapPost("/mcp", HandleHttpJsonRpcAsync);
        endpoints.MapGet("/health", HandleHealthAsync);
        endpoints.MapGet("/ready", HandleReadinessAsync);
        endpoints.MapGet(metricsPath, HandleMetricsAsync);
        return endpoints;
    }

    private static async Task HandleWebSocketUpgradeAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();

        string callerScope;
        try
        {
            callerScope = Authorize(context, state);
        }
        catch (AppErrorException error)
        {
            await WriteErrorAsync(context, error).ConfigureAwait(false);
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "POST";
            return;
        }

        var subprotocol = context.WebSockets.WebSocketRequestedProtocols.Contains("mcp", StringComparer.Ordinal) ? "mcp" : null;
        using var socket = await context.WebSockets.AcceptWebSocketAsync(subprotocol).ConfigureAwait(false);
        var handler = context.RequestServices.GetRequiredService<WebSocketConnectionHandler>();
        await handler.HandleAsync(socket, state, callerScope, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task HandleHttpJsonRpcAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();

        string callerScope;
        try
        {
            callerScope = Authorize(context, state);
        }
        catch (AppErrorException error)
        {
            await WriteErrorAsync(context, error).ConfigureAwait(false);
            return;
        }

        if (HttpProfile.RejectStatus(context.Request) is { } rejected)
        {
            context.Response.StatusCode = rejected;
            return;
        }

        string text;
        using var bodyDeadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        bodyDeadline.CancelAfter(TimeSpan.FromSeconds(state.Config.Limits.HttpBodyTimeoutSecs));
        try
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, bodyDeadline.Token).ConfigureAwait(false);
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(body.GetBuffer(), 0, checked((int)body.Length));
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && bodyDeadline.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
            return;
        }
        catch (DecoderFallbackException)
        {
            // This intentionally mirrors Rust's transport-level response rather than the normal
            // McpMessage serializer: transport decoding failed before an MCP message existed.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await WriteResponseAsync(context,
                "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error: MCP messages must be UTF-8\"}}").ConfigureAwait(false);
            return;
        }

        var dispatcher = context.RequestServices.GetRequiredService<McpDispatcher>();
        var response = await dispatcher.ProcessMessageAsync(text, state, callerScope, context.RequestAborted).ConfigureAwait(false);

        if (response is null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        // Streamable HTTP does not send an in-band JSON-RPC error for rejected one-way
        // notifications or unsolicited client responses. WebSocket and stdio still return the
        // dispatcher error because they do not have an HTTP acknowledgement status.
        if (IsOneWayCandidate(text))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await WriteResponseAsync(context, response).ConfigureAwait(false);
    }

    private static bool IsOneWayCandidate(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            var hasMethod = root.TryGetProperty("method", out _);
            var hasId = root.TryGetProperty("id", out _);
            return (hasMethod && !hasId)
                || (!hasMethod && (hasId || root.TryGetProperty("result", out _)
                    || root.TryGetProperty("error", out _)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task HandleHealthAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        var health = await HealthCheck.RunAsync(state, context.RequestAborted).ConfigureAwait(false);
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await WriteWithDeadlineAsync(context, token => System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, health, McpJson.Options, token))
            .ConfigureAwait(false);
    }

    private static async Task HandleReadinessAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        var capacityAvailable = state.Semaphore.CurrentCount > 0;
        var health = await HealthCheck.RunAsync(state, context.RequestAborted).ConfigureAwait(false);
        var ready = capacityAvailable && health.Status == "healthy";
        context.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await WriteResponseAsync(context, ready ? "ready" : "not ready").ConfigureAwait(false);
    }

    private static Task HandleMetricsAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        return WriteResponseAsync(context, state.Metrics.RenderPrometheus());
    }

    private static string Authorize(HttpContext context, AppState state)
    {
        OriginPolicy.Validate(context.Request.Headers, state.Config.Auth.AllowedOrigins);

        if (!state.Config.Auth.Enabled)
        {
            return "public";
        }

        var hmacKey = state.Config.Auth.CacheScopeHmacKey is { Length: > 0 } key
            ? System.Text.Encoding.UTF8.GetBytes(key)
            : null;
        return BearerAuth.AuthenticatedCacheScope(context.Request.Headers, state.Config.Auth.Tokens, hmacKey);
    }

    private static Task WriteErrorAsync(HttpContext context, AppErrorException error)
    {
        context.Response.StatusCode = error.StatusCode;
        if (error.StatusCode == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }
        return WriteResponseAsync(context, error.Code);
    }
    private static Task WriteResponseAsync(HttpContext context, string response) =>
        WriteWithDeadlineAsync(context, token => context.Response.WriteAsync(response, token));

    internal static async Task WriteWithDeadlineAsync(HttpContext context, Func<CancellationToken, Task> operation)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(state.Config.Limits.HttpResponseWriteTimeoutSecs));
        try
        {
            await operation(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            // The response may already have started. Abort instead of appending a
            // second envelope or retaining the request on an unresponsive writer.
            context.Abort();
        }
    }

}

using System.Net.Mime;
using System.Text;
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
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
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

        string text;
        try
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted).ConfigureAwait(false);
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(body.GetBuffer(), 0, checked((int)body.Length));
        }
        catch (DecoderFallbackException)
        {
            // This intentionally mirrors Rust's transport-level response rather than the normal
            // McpMessage serializer: transport decoding failed before an MCP message existed.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error: MCP messages must be UTF-8\"}}",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var dispatcher = context.RequestServices.GetRequiredService<McpDispatcher>();
        var response = await dispatcher.ProcessMessageAsync(text, state, callerScope, context.RequestAborted).ConfigureAwait(false);

        if (response is null)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(response, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task HandleHealthAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        var health = await HealthCheck.RunAsync(state, context.RequestAborted).ConfigureAwait(false);
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, health, McpJson.Options, context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task HandleReadinessAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        var capacityAvailable = state.Semaphore.CurrentCount > 0;
        var health = await HealthCheck.RunAsync(state, context.RequestAborted).ConfigureAwait(false);
        var ready = capacityAvailable && health.Status == "healthy";
        context.Response.StatusCode = ready ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsync(ready ? "ready" : "not ready", context.RequestAborted).ConfigureAwait(false);
    }

    private static Task HandleMetricsAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        return context.Response.WriteAsync(state.Metrics.RenderPrometheus(), context.RequestAborted);
    }

    private static string Authorize(HttpContext context, AppState state)
    {
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
        return context.Response.WriteAsync(error.Code, context.RequestAborted);
    }
}

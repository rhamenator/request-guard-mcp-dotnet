using System.Net.Mime;
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
    public static IEndpointRouteBuilder MapMcpServer(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/mcp", HandleWebSocketUpgradeAsync);
        endpoints.MapPost("/mcp", HandleHttpJsonRpcAsync);
        endpoints.MapGet("/health", HandleHealthAsync);
        endpoints.MapGet("/ready", HandleReadinessAsync);
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
        using (var reader = new StreamReader(context.Request.Body))
        {
            text = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
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
        var health = HealthCheck.Run(state);
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, health, McpJson.Options, context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static Task HandleReadinessAsync(HttpContext context)
    {
        var state = context.RequestServices.GetRequiredService<AppState>();
        var capacityAvailable = state.Semaphore.CurrentCount > 0;
        // Redis/PostgreSQL/GeoIP readiness checks are added in Phase 4, once those
        // integrations exist (src/mcp/server.rs's readiness_handler).
        context.Response.StatusCode = capacityAvailable ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsync(capacityAvailable ? "ready" : "not ready", context.RequestAborted);
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

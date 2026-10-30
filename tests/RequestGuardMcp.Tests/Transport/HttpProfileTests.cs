using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp;
using RequestGuardMcp.Mcp.Transport;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Transport;

public class HttpProfileTests
{
    private static readonly string[] DuplicateAuthorization = ["Bearer profile-token", "Bearer other"];
    [Theory]
    [InlineData(null, "application/json, text/event-stream", null, 415)]
    [InlineData("text/plain", "application/json, text/event-stream", null, 415)]
    [InlineData("application/json; charset=utf-16", "application/json, text/event-stream", null, 415)]
    [InlineData("application/json", null, null, 406)]
    [InlineData("application/json", "application/json", null, 406)]
    [InlineData("application/json", "*/*", null, 406)]
    [InlineData("application/json", "application/json, text/event-stream;q=0", null, 406)]
    [InlineData("application/json", "application/json, text/event-stream", "2024-11-05", 400)]
    [InlineData("application/json", "application/json, text/event-stream", "", 400)]
    [InlineData("application/json", "application/json, text/event-stream", "2025-06-18", 200)]
    [InlineData("application/json; charset=\"UTF-8\"", "application/json, text/event-stream", null, 200)]
    public async Task NegotiationPrecedesDispatch(string? contentType, string? accept, string? version, int status)
    {
        await using var host = await Host.StartAsync();
        using var request = Request();
        request.Content!.Headers.Remove("Content-Type");
        if (contentType is not null)
        {
            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        request.Headers.Remove("Accept");
        if (accept is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        if (version is not null)
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version);
        }

        using var response = await host.Client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserOriginRequiresAnExplicitAllowlistEvenWithoutAuth(bool auth)
    {
        await using var host = await Host.StartAsync(auth);
        using var request = Request();
        request.Headers.Add("Origin", "https://client.test");
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllowedOriginStillRequiresAuthentication()
    {
        await using var host = await Host.StartAsync(origins: ["https://client.test"]);
        using var request = Request();
        request.Headers.Add("Origin", "https://client.test");
        using var accepted = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var missing = Request();
        missing.Headers.Add("Origin", "https://client.test");
        missing.Headers.Remove("Authorization");
        using var rejected = await host.Client.SendAsync(missing);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal("Bearer", Assert.Single(rejected.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task DuplicateAuthorizationIsRejectedOnTheWire()
    {
        await using var host = await Host.StartAsync();
        using var request = Request();
        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", DuplicateAuthorization);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"health\",\"params\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{},\"error\":null}")]
    [InlineData("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":1.5,\"message\":\"bad\"}}")]
    public async Task RejectedOneWayEnvelopesReturnEmptyBadRequest(string body)
    {
        await using var host = await Host.StartAsync();
        using var request = Request();
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task WebSocketUsesTheSameOriginAndBearerBoundary()
    {
        await using var host = await Host.StartAsync(origins: ["https://client.test"]);
        var address = new Uri(host.Client.BaseAddress!.ToString().Replace("http:", "ws:", StringComparison.Ordinal) + "mcp");
        using var rejected = new ClientWebSocket();
        rejected.Options.SetRequestHeader("Authorization", "Bearer profile-token");
        rejected.Options.SetRequestHeader("Origin", "https://other.test");
        await Assert.ThrowsAsync<WebSocketException>(() => rejected.ConnectAsync(address, CancellationToken.None));
        using var accepted = new ClientWebSocket();
        accepted.Options.SetRequestHeader("Authorization", "bEaReR profile-token");
        accepted.Options.SetRequestHeader("Origin", "https://client.test");
        accepted.Options.AddSubProtocol("mcp");
        await accepted.ConnectAsync(address, CancellationToken.None);
        Assert.Equal("mcp", accepted.SubProtocol);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await accepted.SendAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}"), WebSocketMessageType.Text, true, deadline.Token);
        var bytes = new byte[1024];
        var result = await accepted.ReceiveAsync(bytes, deadline.Token);
        Assert.Contains("\"result\":{}", Encoding.UTF8.GetString(bytes, 0, result.Count), StringComparison.Ordinal);
        await accepted.CloseAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token);
    }

    private static HttpRequestMessage Request()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "mcp")
        {
            Content = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", "Bearer profile-token");
        request.Headers.Add("Accept", "application/json, text/event-stream");
        return request;
    }

    private sealed class Host(WebApplication app, AppState state, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<Host> StartAsync(bool auth = true, List<string>? origins = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = auth, Tokens = ["profile-token"], AllowedOrigins = origins ?? [] } });
            builder.Services.AddSingleton(state);
            builder.Services.AddSingleton(ToolRegistryFactory.Create());
            builder.Services.AddSingleton<McpDispatcher>();
            builder.Services.AddSingleton<WebSocketConnectionHandler>();
            var app = builder.Build();
            app.UseWebSockets();
            app.MapMcpServer();
            await app.StartAsync();
            return new Host(app, state, new HttpClient { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(10) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            await state.DisposeAsync();
        }
    }
}

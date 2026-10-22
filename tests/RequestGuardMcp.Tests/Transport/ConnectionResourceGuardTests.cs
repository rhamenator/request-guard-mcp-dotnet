using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp;
using RequestGuardMcp.Tools;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Mcp.Transport;

namespace RequestGuardMcp.Tests.Transport;

public class ConnectionResourceGuardTests
{
    [Fact]
    public async Task ConcurrentAdmissionHasExactly128Winners()
    {
        using var budget = new ConnectionBudget(new LimitsConfig(), TimeProvider.System, TimeSpan.FromSeconds(120));
        var winners = 0;
        await Parallel.ForEachAsync(Enumerable.Range(0, 1024), (index, token) =>
        {
            if (budget.Admit(http2: true))
            {
                Interlocked.Increment(ref winners);
                budget.Complete(http2: true);
            }

            return ValueTask.CompletedTask;
        });

        Assert.Equal(128, winners);
        Assert.Equal(CloseReason.Http2Limit, await budget.WaitForCloseAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("PRI * HTTP/2.0\r\n")]
    [InlineData("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n")]
    public async Task IncompleteHttp2ConnectionClosesAndCapacityRecovers(string preface)
    {
        var limits = new LimitsConfig { MaxConnections = 1, HttpHeaderTimeoutSecs = 1 };
        await using var host = await RunningServer.StartAsync(limits, HttpProtocols.Http2);
        using var stalled = new TcpClient();
        await stalled.ConnectAsync(IPAddress.Loopback, host.Port);
        await stalled.GetStream().WriteAsync(Encoding.ASCII.GetBytes(preface));
        var reader = stalled.GetStream();
        var buffer = new byte[4096];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            while (await reader.ReadAsync(buffer, deadline.Token) != 0) { }
        }
        catch (IOException)
        {
            // A reset is also a closed socket; cancellation is deliberately not swallowed.
        }

        using var client = host.Client();
        using var healthy = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Fact]
    public async Task IdleHttp2ConnectionIsReplacedAfterCompletedRequest()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig { Http2IdleTimeoutSecs = 1 }, HttpProtocols.Http2);
        using var client = host.Client();
        var first = await client.GetStringAsync("/");
        await Task.Delay(TimeSpan.FromSeconds(3));
        var next = await client.GetStringAsync("/");

        Assert.NotEqual(first, next);
    }

    [Fact]
    public async Task ContinuousHttp2TrafficCannotAvoidAgeLimit()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig { Http2IdleTimeoutSecs = 60 }, HttpProtocols.Http2,
            lifetime: TimeSpan.FromSeconds(1));
        using var client = host.Client();
        var first = await client.GetStringAsync("/");
        var replaced = false;
        for (var index = 0; index < 20; index++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            if (await client.GetStringAsync("/") != first)
            {
                replaced = true;
                break;
            }
        }

        Assert.True(replaced, "traffic must not retain a connection past its age budget");
    }

    [Fact]
    public async Task FirstRequestTimerDoesNotLimitHttp1KeepAliveLifetime()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig { HttpHeaderTimeoutSecs = 1 }, HttpProtocols.Http1);
        using var client = host.Client(http2: false);
        var first = await client.GetStringAsync("/");
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(first, await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task ExcessRequestGets503BeforeDownstreamCodeRuns()
    {
        using var guard = new ConnectionResourceGuard(new LimitsConfig());
        await using var connection = new DefaultConnectionContext("cap-test");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = guard.HandleConnectionAsync(connection, _ =>
        {
            started.SetResult();
            return done.Task;
        });
        await started.Task;
        var calls = 0;
        try
        {
            for (var index = 0; index < 129; index++)
            {
                var context = new DefaultHttpContext();
                context.Connection.Id = "cap-test";
                context.Request.Protocol = "HTTP/2";
                var response = guard.InvokeAsync(context, _ => { calls++; return Task.CompletedTask; });
                Assert.True(response.IsCompleted);
                await response;
                Assert.Equal(index < 128 ? 200 : 503, context.Response.StatusCode);
            }

            Assert.Equal(128, calls);
        }
        finally
        {
            done.SetResult();
            await serving;
        }
    }

    [Fact]
    public async Task Http2RequestCapRotatesRealConnection()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig(), HttpProtocols.Http2);
        using var client = host.Client();
        var first = await client.GetStringAsync("/");
        for (var index = 1; index < 128; index++)
        {
            Assert.Equal(first, await client.GetStringAsync("/"));
        }

        Assert.NotEqual(first, await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task IncompleteHttpBodyReturns408WithinItsBudget()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig { HttpBodyTimeoutSecs = 1 }, HttpProtocols.Http1);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, host.Port);
        await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
            "POST /mcp HTTP/1.1\r\nHost: localhost\r\nContent-Length: 100\r\nConnection: close\r\n\r\n{"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var buffer = new byte[4096];
        var read = await client.GetStream().ReadAsync(buffer, deadline.Token);

        Assert.Contains("408", Encoding.ASCII.GetString(buffer, 0, read));
    }

    [Fact]
    public async Task Http2GracefulDrainCannotBeHeldByAnUnresponsivePeer()
    {
        await using var host = await RunningServer.StartAsync(new LimitsConfig { MaxConnections = 1, Http2IdleTimeoutSecs = 1 }, HttpProtocols.Http2);
        using var peer = new TcpClient();
        await peer.ConnectAsync(IPAddress.Loopback, host.Port);
        var stream = peer.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"));
        await stream.WriteAsync(new byte[] { 0, 0, 0, 4, 0, 0, 0, 0, 0 }); // SETTINGS
        // HPACK: GET /, http, literal :authority localhost. No dynamic table needed.
        byte[] headers = [0x82, 0x84, 0x86, 0x01, 0x09, .. Encoding.ASCII.GetBytes("localhost")];
        await stream.WriteAsync(new byte[] { 0, 0, (byte)headers.Length, 1, 5, 0, 0, 0, 1 });
        await stream.WriteAsync(headers);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var goAway = false;
        var header = new byte[9];
        while (true)
        {
            var read = await stream.ReadAsync(header.AsMemory(0, 1), deadline.Token);
            if (read == 0)
            {
                break;
            }

            await stream.ReadExactlyAsync(header.AsMemory(1), deadline.Token);
            var size = (header[0] << 16) | (header[1] << 8) | header[2];
            var payload = new byte[size];
            await stream.ReadExactlyAsync(payload, deadline.Token);
            goAway |= header[3] == 7;
            if (header[3] == 4 && header[4] == 0)
            {
                await stream.WriteAsync(new byte[] { 0, 0, 0, 4, 1, 0, 0, 0, 0 }, deadline.Token);
            }

            // Deliberately do not acknowledge PING/GOAWAY with more traffic.
        }

        Assert.True(goAway, "graceful close must emit GOAWAY before bounded socket teardown");
        using var client = host.Client();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class RunningServer(WebApplication app, ConnectionResourceGuard guard, int port) : IAsyncDisposable
    {
        public int Port => port;

        public HttpClient Client(bool http2 = true) => new()
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            DefaultRequestVersion = http2 ? HttpVersion.Version20 : HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Timeout = TimeSpan.FromSeconds(8),
        };

        public static async Task<RunningServer> StartAsync(LimitsConfig limits, HttpProtocols protocols, TimeSpan? lifetime = null)
        {
            var guard = new ConnectionResourceGuard(limits, TimeProvider.System, lifetime ?? TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(1));
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                guard.ConfigureKestrel(options);
                options.Listen(IPAddress.Loopback, 0, endpoint => endpoint.Protocols = protocols);
            });
            builder.Services.AddSingleton(new AppState(new AppConfig { Limits = limits, Auth = new AuthConfig { Enabled = false } }));
            builder.Services.AddSingleton(ToolRegistryFactory.Create());
            builder.Services.AddSingleton<McpDispatcher>();
            builder.Services.AddSingleton<WebSocketConnectionHandler>();
            var app = builder.Build();
            app.Use(guard.InvokeAsync);
            app.UseWebSockets();
            app.MapMcpServer();
            app.MapGet("/", context => context.Response.WriteAsync(context.Connection.Id));
            await app.StartAsync();
            return new RunningServer(app, guard, new Uri(app.Urls.Single()).Port);
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            guard.Dispose();
        }
    }
}

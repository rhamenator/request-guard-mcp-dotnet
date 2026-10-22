using System.Collections.Concurrent;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Mcp.Transport;

/// <summary>Pre-authentication connection admission and HTTP/2 lifetime bounds.</summary>
public sealed class ConnectionResourceGuard : IDisposable
{
    internal const int Http2RequestCap = 128;
    private readonly LimitsConfig _limits;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _http2Lifetime;
    private readonly TimeSpan _drain;
    private readonly SemaphoreSlim _permits;
    private readonly ConcurrentDictionary<string, ConnectionBudget> _connections = new(StringComparer.Ordinal);

    public ConnectionResourceGuard(LimitsConfig limits)
        : this(limits, TimeProvider.System, TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds((long)limits.HttpBodyTimeoutSecs + Math.Max(limits.PerToolTimeoutSecs, limits.ClassifyTimeoutSecs) + 2))
    {
    }

    internal ConnectionResourceGuard(LimitsConfig limits, TimeProvider clock, TimeSpan http2Lifetime, TimeSpan drain)
    {
        _limits = limits;
        _clock = clock;
        _http2Lifetime = http2Lifetime;
        _drain = drain;
        _permits = new SemaphoreSlim(limits.MaxConnections, limits.MaxConnections);
    }

    public void ConfigureKestrel(KestrelServerOptions options)
    {
        options.Limits.MaxRequestBodySize = _limits.MaxRequestBytes;
        options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(_limits.HttpHeaderTimeoutSecs);
        options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(_limits.Http2IdleTimeoutSecs);
        options.Limits.Http2.MaxStreamsPerConnection = 16;
        options.ConfigureEndpointDefaults(endpoint => endpoint.Use(next => connection => HandleConnectionAsync(connection, next)));
    }

    internal async Task HandleConnectionAsync(ConnectionContext connection, ConnectionDelegate next)
    {
        if (!_permits.Wait(0, connection.ConnectionClosed))
        {
            connection.Abort();
            return;
        }

        using var budget = new ConnectionBudget(_limits, _clock, _http2Lifetime);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(connection.ConnectionClosed);
        _connections[connection.ConnectionId] = budget;
        var monitor = budget.WaitForCloseAsync(stop.Token);
        try
        {
            var processing = next(connection);
            if (await Task.WhenAny(processing, monitor).ConfigureAwait(false) == monitor)
            {
                var reason = await monitor.ConfigureAwait(false);
                if (reason == CloseReason.FirstRequestTimeout)
                {
                    connection.Abort();
                }
                else if (reason == CloseReason.Http2Limit)
                {
                    // Kestrel sends GOAWAY and drains accepted streams. A peer that does not
                    // finish the exchange cannot keep the socket beyond the drain budget.
                    connection.Features.Get<IConnectionLifetimeNotificationFeature>()?.RequestClose();
                    if (await Task.WhenAny(processing, Task.Delay(_drain, stop.Token)).ConfigureAwait(false) != processing)
                    {
                        connection.Abort();
                    }
                }
            }

            await processing.ConfigureAwait(false);
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(false);
            try
            {
                await monitor.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Connection teardown is expected and does not create per-peer logs.
            }

            _connections.TryRemove(connection.ConnectionId, out _);
            _permits.Release();
        }
    }

    /// <summary>Install before authentication, routing and WebSocket upgrades.</summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!_connections.TryGetValue(context.Connection.Id, out var budget))
        {
            // Fail closed if a host forgot to install the connection middleware.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var http2 = context.Request.Protocol == "HTTP/2";
        if (!budget.Admit(http2))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            budget.Complete(http2);
        }
    }

    public void Dispose() => _permits.Dispose();
}

internal enum CloseReason { Http1, FirstRequestTimeout, Http2Limit }

internal sealed class ConnectionBudget(LimitsConfig limits, TimeProvider clock, TimeSpan http2Lifetime) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _changed = new(0, 1);
    private readonly long _accepted = clock.GetTimestamp();
    private long _first;
    private long _idleSince;
    private bool _seen;
    private bool _http2;
    private int _requests;
    private int _active;

    public bool Admit(bool http2)
    {
        lock (_gate)
        {
            if (!_seen)
            {
                _seen = true;
                _http2 = http2;
                _first = clock.GetTimestamp();
            }

            if (http2)
            {
                if (_requests >= ConnectionResourceGuard.Http2RequestCap)
                {
                    return false;
                }

                _requests++;
                _active++;
            }

            Signal();
            return true;
        }
    }

    public void Complete(bool http2)
    {
        if (!http2)
        {
            return;
        }

        lock (_gate)
        {
            _active--;
            _idleSince = clock.GetTimestamp();
            Signal();
        }
    }

    public async Task<CloseReason> WaitForCloseAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan remaining;
            lock (_gate)
            {
                if (!_seen)
                {
                    remaining = TimeSpan.FromSeconds(limits.HttpHeaderTimeoutSecs) + TimeSpan.FromMilliseconds(250) - clock.GetElapsedTime(_accepted);
                    if (remaining <= TimeSpan.Zero)
                    {
                        return CloseReason.FirstRequestTimeout;
                    }
                }
                else
                {
                    if (!_http2)
                    {
                        return CloseReason.Http1;
                    }

                    remaining = http2Lifetime - clock.GetElapsedTime(_first);
                    if (_active == 0)
                    {
                        var idle = TimeSpan.FromSeconds(limits.Http2IdleTimeoutSecs) - clock.GetElapsedTime(_idleSince);
                        remaining = remaining < idle ? remaining : idle;
                    }

                    if (_requests >= ConnectionResourceGuard.Http2RequestCap || remaining <= TimeSpan.Zero)
                    {
                        return CloseReason.Http2Limit;
                    }
                }
            }

            remaining = remaining < TimeSpan.FromMilliseconds(int.MaxValue) ? remaining : TimeSpan.FromMilliseconds(int.MaxValue);
            _ = await _changed.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private void Signal()
    {
        if (_changed.CurrentCount == 0)
        {
            _changed.Release();
        }
    }

    public void Dispose() => _changed.Dispose();
}

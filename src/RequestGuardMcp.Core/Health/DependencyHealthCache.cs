using RequestGuardMcp.Core.Models.Response;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Core.Health;

/// <summary>
/// Shared, bounded dependency health (issue 82). Concurrent callers join one in-flight check, a result is reused for a short
/// time, and the whole check has a hard deadline, so repeated unauthenticated <c>/ready</c> requests cannot turn into one
/// Redis/PostgreSQL probe each or hold backend capacity while a dependency hangs.
/// </summary>
public sealed class DependencyHealthCache(TimeSpan ttl, TimeSpan deadline, TimeProvider clock)
{
    private readonly object _gate = new();
    private Task<HealthResponse>? _inFlight;
    private HealthResponse? _last;
    private long _lastAt;

    public Task<HealthResponse> GetAsync(AppState state, CancellationToken cancellationToken)
    {
        Task<HealthResponse> shared;
        TaskCompletionSource<HealthResponse>? starter = null;
        lock (_gate)
        {
            if (_last is not null && clock.GetElapsedTime(_lastAt) < ttl)
            {
                return Task.FromResult(_last);
            }

            if (_inFlight is null)
            {
                // Publish the in-flight task before any work starts, so a check that finishes synchronously cannot clear
                // the marker before it is set (which would leave a stale completed task behind forever).
                starter = new TaskCompletionSource<HealthResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight = starter.Task;
            }

            shared = _inFlight;
        }

        if (starter is not null)
        {
            _ = RunAsync(state, starter);
        }

        // A caller that gives up stops waiting; the shared check keeps running for the others and ends at its own deadline.
        return shared.WaitAsync(cancellationToken);
    }

    private async Task RunAsync(AppState state, TaskCompletionSource<HealthResponse> completion)
    {
        HealthResponse result;
        try
        {
            using var timeout = new CancellationTokenSource(deadline, clock);
            result = await HealthCheck.RunAsync(state, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deadline expiry or any dependency failure: report unhealthy rather than propagate to unauthenticated callers.
            result = HealthCheck.TimedOut(state);
        }

        lock (_gate)
        {
            _last = result;
            _lastAt = clock.GetTimestamp();
            _inFlight = null;
        }

        completion.SetResult(result);
    }
}

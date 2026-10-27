using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Mcp;

namespace RequestGuardMcp.Tests.Transport;

public class HttpResponseDeadlineTests
{
    [Fact]
    public async Task IgnoredWriteCancellationStillAbortsResponseWithinBudget()
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false }, Limits = new LimitsConfig { HttpResponseWriteTimeoutSecs = 1 } });
        using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        var lifetime = new Lifetime();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await McpEndpoints.WriteWithDeadlineAsync(context, _ => stalled.Task).WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(lifetime.Aborted);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        await using var state = new AppState(new AppConfig { Auth = new AuthConfig { Enabled = false } });
        using var services = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        using var cancel = new CancellationTokenSource();
        var lifetime = new Lifetime { RequestAborted = cancel.Token };
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpRequestLifetimeFeature>(lifetime);
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = McpEndpoints.WriteWithDeadlineAsync(context, _ => stalled.Task);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(lifetime.Aborted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void InvalidResponseTimeoutFailsConfiguration(int seconds) =>
        Assert.Throws<ConfigValidationException>(() => new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { HttpResponseWriteTimeoutSecs = seconds },
        }.Validate());

    private sealed class Lifetime : IHttpRequestLifetimeFeature
    {
        public bool Aborted { get; private set; }
        public CancellationToken RequestAborted { get; set; }
        public void Abort() => Aborted = true;
    }
}

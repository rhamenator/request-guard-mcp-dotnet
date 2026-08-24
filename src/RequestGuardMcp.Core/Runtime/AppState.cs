using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Core.Runtime;

/// <summary>
/// Shared application state passed to every request handler and tool. Ports src/state.rs's
/// <c>AppState</c>. Cache/Redis/PostgreSQL/GeoIP/metrics fields are added in the delivery phases
/// that implement those integrations and observability (phases 3-6).
/// </summary>
public sealed class AppState
{
    public AppConfig Config { get; }
    public SemaphoreSlim Semaphore { get; }
    public BuildInfo BuildInfo { get; }

    public AppState(AppConfig config)
    {
        Config = config;
        Semaphore = new SemaphoreSlim(config.Limits.GlobalConcurrency, config.Limits.GlobalConcurrency);
        BuildInfo = BuildInfo.Current();
    }
}

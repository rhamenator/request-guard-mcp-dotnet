using System.Collections.Concurrent;
using RequestGuardMcp.Core.Caching;
using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Core.Runtime;

/// <summary>
/// Shared application state passed to every request handler and tool. Ports src/state.rs's
/// <c>AppState</c>. Redis/PostgreSQL/GeoIP/metrics fields are added in the delivery phases that
/// implement those integrations and observability (phases 4/6).
/// </summary>
public sealed class AppState
{
    public AppConfig Config { get; }
    public SemaphoreSlim Semaphore { get; }
    public BuildInfo BuildInfo { get; }
    public ICacheStore Cache { get; }

    /// <summary>
    /// Reflects which optional tools/integrations are actually available right now. Ports
    /// src/state.rs's feature-flag seeding — with every flag honestly `false` until the backend
    /// it depends on exists (phases 4-5 flip these on as Redis/PostgreSQL/GeoIP land).
    /// </summary>
    public ConcurrentDictionary<string, bool> FeatureFlags { get; } = new(StringComparer.Ordinal);

    public AppState(AppConfig config)
    {
        Config = config;
        Semaphore = new SemaphoreSlim(config.Limits.GlobalConcurrency, config.Limits.GlobalConcurrency);
        BuildInfo = BuildInfo.Current();
        Cache = new InMemoryCacheStore();

        FeatureFlags["batch_classify"] = true;
        FeatureFlags["enrichment"] = false;
        FeatureFlags["feedback"] = false;
        FeatureFlags["replay_decision"] = false;
        FeatureFlags["drift_report"] = false;
        FeatureFlags["calibration_report"] = false;
        FeatureFlags["canary_eval"] = false;
        FeatureFlags["threat_lookup"] = false;
        FeatureFlags["queue_status"] = false;
        FeatureFlags["geoip"] = false;
    }
}

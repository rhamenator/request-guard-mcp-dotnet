using System.Collections.Concurrent;
using RequestGuardMcp.Core.Caching;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using RequestGuardMcp.Core.Observability;

namespace RequestGuardMcp.Core.Runtime;

/// <summary>
/// Shared application state passed to every request handler and tool. Ports src/state.rs's
/// <c>AppState</c>. Redis/PostgreSQL/GeoIP start in their disabled state (the <c>Null*</c>
/// implementations); the Host composition root replaces them with real, connected clients during
/// startup (see <see cref="AttachIntegrations"/>) before the server begins accepting requests —
/// mirroring the Rust server's async <c>AppState::initialize</c>, split into a sync constructor
/// plus one startup-only mutation step because .NET's DI container needs a concrete instance to
/// register before those async connections can be attempted.
/// </summary>
public sealed class AppState : IAsyncDisposable
{
    public AppConfig Config { get; }
    public SemaphoreSlim Semaphore { get; }
    public BuildInfo BuildInfo { get; }
    public ICacheStore Cache { get; }
    public IRedisClient Redis { get; private set; } = NullRedisClient.Instance;
    public IPostgresClient Postgres { get; private set; } = NullPostgresClient.Instance;
    public IGeoipClient Geoip { get; private set; } = NullGeoipClient.Instance;
    public IReputationClient Reputation { get; private set; }
    public AppMetrics Metrics { get; } = new();

    public ConcurrentDictionary<string, bool> FeatureFlags { get; } = new(StringComparer.Ordinal);

    public AppState(AppConfig config)
    {
        Config = config;
        Semaphore = new SemaphoreSlim(config.Limits.GlobalConcurrency, config.Limits.GlobalConcurrency);
        BuildInfo = BuildInfo.Current();
        Cache = new InMemoryCacheStore();
        Reputation = new ReputationClient(Redis);
        RefreshFeatureFlags();
    }

    /// <summary>
    /// Startup-only: replaces the disabled-state integration clients with real, connected ones.
    /// Must be called before the server begins accepting requests, never afterward.
    /// </summary>
    public void AttachIntegrations(IRedisClient? redis = null, IPostgresClient? postgres = null, IGeoipClient? geoip = null)
    {
        if (redis is not null)
        {
            Redis = redis;
            Reputation = new ReputationClient(redis);
        }

        if (postgres is not null)
        {
            Postgres = postgres;
        }

        if (geoip is not null)
        {
            Geoip = geoip;
        }

        RefreshFeatureFlags();
    }

    /// <summary>Ports src/state.rs's feature-flag seeding, re-run whenever an integration is attached.</summary>
    private void RefreshFeatureFlags()
    {
        FeatureFlags["batch_classify"] = Config.Features.EnableBatch;
        FeatureFlags["enrichment"] = Config.Features.EnableEnrichment;
        FeatureFlags["feedback"] = Config.Features.EnableFeedback && Postgres.IsAvailable;
        FeatureFlags["replay_decision"] = Postgres.IsAvailable;
        FeatureFlags["drift_report"] = Postgres.IsAvailable;
        FeatureFlags["calibration_report"] = Postgres.IsAvailable;
        FeatureFlags["canary_eval"] = Redis.IsAvailable;
        FeatureFlags["threat_lookup"] = Redis.IsAvailable;
        FeatureFlags["queue_status"] = Redis.IsAvailable;
        FeatureFlags["geoip"] = Geoip.IsAvailable;
    }

    public async ValueTask DisposeAsync()
    {
        if (Redis is IAsyncDisposable asyncRedis)
        {
            await asyncRedis.DisposeAsync().ConfigureAwait(false);
        }

        if (Postgres is IAsyncDisposable asyncPostgres)
        {
            await asyncPostgres.DisposeAsync().ConfigureAwait(false);
        }

        if (Geoip is IDisposable disposableGeoip)
        {
            disposableGeoip.Dispose();
        }

        Semaphore.Dispose();
    }
}

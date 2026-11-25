using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;

namespace RequestGuardMcp.Host;

/// <summary>
/// Two different builds that both report an unknown source revision would share one distributed cache namespace and serve
/// each other's classifications (issue 80). Release builds embed the revision; any other build that uses Redis must name its cache.
/// </summary>
internal static class CacheIdentityGuard
{
    internal static string? Problem(AppConfig config, BuildInfo build) =>
        config.Redis.Url is not null && build.HasUnknownCommit && string.IsNullOrWhiteSpace(config.Redis.CacheDomain)
            ? "this build has no embedded source revision, so MCP__REDIS__CACHE_DOMAIN must be set to a unique name when Redis is configured"
            : null;
}

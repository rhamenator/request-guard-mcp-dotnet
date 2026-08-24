using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Caching;

/// <summary>The in-process classification cache. Ports src/integrations/cache.rs's <c>CacheStore</c>.</summary>
public interface ICacheStore
{
    public Task<JsonNode?> GetAsync(string key, CancellationToken cancellationToken = default);

    public Task SetAsync(string key, JsonNode value, CancellationToken cancellationToken = default);
}

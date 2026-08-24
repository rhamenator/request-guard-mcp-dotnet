using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Caching;

/// <summary>
/// A bounded, TTL-expiring in-process cache. Ports src/integrations/cache.rs's <c>CacheStore</c>,
/// which wraps the <c>moka</c> crate's async LRU cache; this is a straightforward
/// capacity-bounded (FIFO eviction, not full LRU) TTL cache built on the BCL alone, since a
/// hand-rolled cache is simple enough here to avoid an extra dependency for something this small.
/// If eviction quality ever becomes a real bottleneck, swap the implementation behind
/// <see cref="ICacheStore"/> without touching call sites.
/// </summary>
public sealed class InMemoryCacheStore(int maxCapacity = 10_000, TimeSpan? timeToLive = null) : ICacheStore
{
    private readonly TimeSpan _timeToLive = timeToLive ?? TimeSpan.FromSeconds(300);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _insertionOrder = new();

    private sealed record Entry(JsonNode Value, DateTimeOffset ExpiresAt);

    public Task<JsonNode?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return Task.FromResult<JsonNode?>(entry.Value);
            }

            _entries.TryRemove(key, out _);
        }

        return Task.FromResult<JsonNode?>(null);
    }

    public Task SetAsync(string key, JsonNode value, CancellationToken cancellationToken = default)
    {
        var isNewKey = !_entries.ContainsKey(key);
        _entries[key] = new Entry(value, DateTimeOffset.UtcNow + _timeToLive);
        if (isNewKey)
        {
            _insertionOrder.Enqueue(key);
        }

        while (_entries.Count > maxCapacity && _insertionOrder.TryDequeue(out var oldest))
        {
            _entries.TryRemove(oldest, out _);
        }

        return Task.CompletedTask;
    }
}

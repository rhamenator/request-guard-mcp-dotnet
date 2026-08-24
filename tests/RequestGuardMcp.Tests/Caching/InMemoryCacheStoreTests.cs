using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Caching;

namespace RequestGuardMcp.Tests.Caching;

public class InMemoryCacheStoreTests
{
    [Fact]
    public async Task SetThenGetReturnsTheStoredValue()
    {
        var cache = new InMemoryCacheStore();

        await cache.SetAsync("key", JsonValue.Create("value")!);
        var result = await cache.GetAsync("key");

        Assert.Equal("value", result!.GetValue<string>());
    }

    [Fact]
    public async Task MissingKeyReturnsNull()
    {
        var cache = new InMemoryCacheStore();

        Assert.Null(await cache.GetAsync("missing"));
    }

    [Fact]
    public async Task ExpiredEntryIsNotReturned()
    {
        var cache = new InMemoryCacheStore(timeToLive: TimeSpan.FromMilliseconds(1));

        await cache.SetAsync("key", JsonValue.Create("value")!);
        await Task.Delay(50);

        Assert.Null(await cache.GetAsync("key"));
    }

    [Fact]
    public async Task CapacityBoundEvictsOldestEntries()
    {
        var cache = new InMemoryCacheStore(maxCapacity: 2);

        await cache.SetAsync("a", JsonValue.Create(1)!);
        await cache.SetAsync("b", JsonValue.Create(2)!);
        await cache.SetAsync("c", JsonValue.Create(3)!);

        Assert.Null(await cache.GetAsync("a"));
        Assert.NotNull(await cache.GetAsync("c"));
    }
}

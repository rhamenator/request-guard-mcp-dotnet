using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Integrations;
using StackExchange.Redis;

namespace RequestGuardMcp.Integrations;

/// <summary>
/// The Redis-backed distributed cache, threat registry, canary registry, and queue telemetry.
/// Ports src/integrations/redis.rs's <c>RedisClient</c> (the <c>redis-integration</c> feature's
/// inner module) using StackExchange.Redis, whose <see cref="IConnectionMultiplexer"/> already
/// multiplexes commands over its own managed connection(s) — there is no separate pool size to
/// configure the way deadpool-redis needs one, so <see cref="RedisConfig.PoolSize"/> is accepted
/// for configuration-compatibility with the Rust deployment examples but not otherwise used here.
/// Only ever constructed via <see cref="ConnectAsync"/> when a URL is configured — the disabled
/// state is represented separately by <see cref="Core.Integrations.NullRedisClient"/>.
/// </summary>
public sealed class RedisClient : IRedisClient, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly string _keyPrefix;
    private readonly int _cacheTtlSecs;

    private RedisClient(IConnectionMultiplexer multiplexer, string keyPrefix, int cacheTtlSecs)
    {
        _multiplexer = multiplexer;
        _keyPrefix = keyPrefix;
        _cacheTtlSecs = cacheTtlSecs;
    }

    public static async Task<RedisClient> ConnectAsync(RedisConfig config, CancellationToken cancellationToken = default)
    {
        var options = ParseOptions(config.Url!);
        options.AbortOnConnectFail = true;
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(cancellationToken).ConfigureAwait(false);
        var client = new RedisClient(multiplexer, config.KeyPrefix, config.CacheTtlSecs);
        if (!await client.PingAsync(cancellationToken).ConfigureAwait(false))
        {
            await multiplexer.CloseAsync().ConfigureAwait(false);
            throw new InvalidOperationException("configured Redis did not respond to PING");
        }

        return client;
    }

    private static ConfigurationOptions ParseOptions(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("redis" or "rediss"))
        {
            return ConfigurationOptions.Parse(value);
        }

        var options = new ConfigurationOptions { Ssl = uri.Scheme == "rediss" };
        options.EndPoints.Add(uri.Host, uri.IsDefaultPort ? 6379 : uri.Port);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var credentials = uri.UserInfo.Split(':', 2);
            if (credentials.Length == 1)
            {
                options.Password = Uri.UnescapeDataString(credentials[0]);
            }
            else
            {
                if (credentials[0].Length > 0)
                {
                    options.User = Uri.UnescapeDataString(credentials[0]);
                }

                options.Password = Uri.UnescapeDataString(credentials[1]);
            }
        }

        if (uri.AbsolutePath is { Length: > 1 } path && int.TryParse(path[1..], System.Globalization.CultureInfo.InvariantCulture, out var database))
        {
            options.DefaultDatabase = database;
        }

        return options;
    }

    public bool IsAvailable => true;

    private IDatabase Database => _multiplexer.GetDatabase();

    private string Key(string suffix) => $"{_keyPrefix}:{suffix}";

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Database.PingAsync().ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<JsonNode?> CacheGetAsync(string fingerprint, CancellationToken cancellationToken = default)
    {
        var value = await Database.StringGetAsync(Key($"cache:{fingerprint}")).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : JsonNode.Parse(value.ToString());
    }

    public async Task CacheSetAsync(string fingerprint, JsonNode value, CancellationToken cancellationToken = default)
    {
        await Database.StringSetAsync(Key($"cache:{fingerprint}"), value.ToJsonString(), TimeSpan.FromSeconds(_cacheTtlSecs)).ConfigureAwait(false);
    }

    public async Task<ThreatRecord?> ThreatLookupAsync(string indicatorType, string indicator, CancellationToken cancellationToken = default)
    {
        var value = await Database.HashGetAsync(Key($"threats:{indicatorType}"), indicator).ConfigureAwait(false);
        if (!value.IsNullOrEmpty)
        {
            return System.Text.Json.JsonSerializer.Deserialize<ThreatRecord>(value.ToString(), Core.Json.McpJson.Options);
        }

        if (indicatorType == "ip")
        {
            var listed = await Database.SetContainsAsync(Key("blocklist:ips"), indicator).ConfigureAwait(false);
            if (listed)
            {
                return new ThreatRecord("blocklisted_ip", "high", "redis_blocklist", null);
            }
        }

        return null;
    }

    public async Task StoreThreatAsync(string indicatorType, string indicator, ThreatRecord record, CancellationToken cancellationToken = default)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(record, Core.Json.McpJson.Options);
        await Database.HashSetAsync(Key($"threats:{indicatorType}"), indicator, json).ConfigureAwait(false);
    }

    public async Task<CanaryRecord?> CanaryLookupAsync(string token, JsonNode? context, CancellationToken cancellationToken = default)
    {
        var tokenHash = Sha256Hex(token);
        var value = await Database.HashGetAsync(Key("canaries"), tokenHash).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var record = System.Text.Json.JsonSerializer.Deserialize<CanaryRecord>(value.ToString(), Core.Json.McpJson.Options);
        var eventJson = new JsonObject
        {
            ["canary_id"] = record?.CanaryId,
            ["token_hash"] = tokenHash,
            ["triggered_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["context"] = BoundedContext(context),
        }.ToJsonString();

        var eventsKey = Key("canary_events");
        await Database.ListLeftPushAsync(eventsKey, eventJson).ConfigureAwait(false);
        await Database.ListTrimAsync(eventsKey, 0, 9_999).ConfigureAwait(false);

        return record;
    }

    public async Task RegisterCanaryAsync(string token, CanaryRecord record, CancellationToken cancellationToken = default)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(record, Core.Json.McpJson.Options);
        await Database.HashSetAsync(Key("canaries"), Sha256Hex(token), json).ConfigureAwait(false);
    }

    public async Task<string> RecordToolStartedAsync(string tool, long timeoutSecs, CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var now = UnixSeconds();
        var activeKey = Key($"queue:{tool}:active");

        var transaction = Database.CreateTransaction();
        _ = transaction.SortedSetRemoveRangeByScoreAsync(activeKey, double.NegativeInfinity, now - timeoutSecs * 2.0);
        _ = transaction.SortedSetAddAsync(activeKey, operationId, now);
        _ = transaction.KeyExpireAsync(activeKey, TimeSpan.FromSeconds(Math.Max(timeoutSecs * 4, 60)));
        await transaction.ExecuteAsync().ConfigureAwait(false);

        return operationId;
    }

    public async Task RecordToolFinishedAsync(string tool, string operationId, CancellationToken cancellationToken = default)
    {
        var now = UnixSeconds();
        var activeKey = Key($"queue:{tool}:active");
        var completedKey = Key($"queue:{tool}:completed");

        var transaction = Database.CreateTransaction();
        _ = transaction.SortedSetRemoveAsync(activeKey, operationId);
        _ = transaction.SortedSetAddAsync(completedKey, operationId, now);
        _ = transaction.SortedSetRemoveRangeByScoreAsync(completedKey, double.NegativeInfinity, now - 300.0);
        _ = transaction.KeyExpireAsync(completedKey, TimeSpan.FromSeconds(600));
        await transaction.ExecuteAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QueueStats>> QueueStatsAsync(string? requested, CancellationToken cancellationToken = default)
    {
        var names = requested is not null ? [requested] : await ScanQueueNamesAsync().ConfigureAwait(false);
        var now = UnixSeconds();
        var stats = new List<QueueStats>(names.Count);

        foreach (var name in names)
        {
            var active = await Database.SortedSetLengthAsync(Key($"queue:{name}:active")).ConfigureAwait(false);
            var completedLastMinute = await Database.SortedSetLengthAsync(
                Key($"queue:{name}:completed"), now - 60.0, double.PositiveInfinity).ConfigureAwait(false);
            stats.Add(new QueueStats(name, (ulong)active, (ulong)completedLastMinute));
        }

        stats.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return stats;
    }

    private async Task<List<string>> ScanQueueNamesAsync()
    {
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints()[0]);
        var prefix = Key("queue:");
        var names = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var key in server.KeysAsync(pattern: Key("queue:*:active")))
        {
            var text = key.ToString();
            if (text.StartsWith(prefix, StringComparison.Ordinal) && text.EndsWith(":active", StringComparison.Ordinal))
            {
                names.Add(text[prefix.Length..^":active".Length]);
            }
        }

        var sorted = names.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }

    private static double UnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    private static string Sha256Hex(string input) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static JsonNode? BoundedContext(JsonNode? context)
    {
        if (context is null)
        {
            return null;
        }

        var encoded = Encoding.UTF8.GetBytes(context.ToJsonString());
        if (encoded.Length <= 4096)
        {
            return context.DeepClone();
        }

        return new JsonObject
        {
            ["truncated"] = true,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(encoded)),
            ["original_bytes"] = encoded.Length,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _multiplexer.CloseAsync().ConfigureAwait(false);
        _multiplexer.Dispose();
    }
}

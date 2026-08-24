using RequestGuardMcp.Core.Util;

namespace RequestGuardMcp.Core.Configuration;

/// <summary>
/// Top-level application configuration. Ports src/config.rs's <c>Config</c>.
/// </summary>
public sealed class AppConfig
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8085;
    public AuthConfig Auth { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
    public RedisConfig Redis { get; set; } = new();
    public PostgresConfig Postgres { get; set; } = new();
    public GeoipConfig Geoip { get; set; } = new();
    public FeatureConfig Features { get; set; } = new();
    public TelemetryConfig Telemetry { get; set; } = new();
    public TlsFingerprintConfig TlsFingerprints { get; set; } = new();
    public string LogLevel { get; set; } = "info";

    public TimeSpan PerToolTimeout => TimeSpan.FromSeconds(Limits.PerToolTimeoutSecs);

    public TimeSpan ClassifyTimeout => TimeSpan.FromSeconds(Limits.ClassifyTimeoutSecs);

    /// <summary>
    /// Validates invariants that must hold before the server starts. Throws
    /// <see cref="ConfigValidationException"/> on failure, mirroring src/config.rs's
    /// <c>validate()</c> (via <c>anyhow::bail!</c>).
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535)
        {
            throw new ConfigValidationException("host must be non-empty and port must be between 1 and 65535");
        }

        if (Auth.Enabled)
        {
            if (Auth.Tokens.Count == 0)
            {
                throw new ConfigValidationException("authentication is enabled but AUTH_TOKENS is empty");
            }

            if (Auth.Tokens.Any(token =>
                    string.IsNullOrWhiteSpace(token) ||
                    token.Equals("replace_me", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("replace_me_with_a_strong_token", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ConfigValidationException("AUTH_TOKENS contains an empty or placeholder token");
            }
        }

        ValidateSecretLength(Auth.CacheScopeHmacKey, "CACHE_SCOPE_HMAC_KEY");

        if (Limits.MaxRequestBytes <= 0 ||
            Limits.MaxBatchSize <= 0 ||
            Limits.GlobalConcurrency <= 0 ||
            Limits.PerToolTimeoutSecs <= 0 ||
            Limits.ClassifyTimeoutSecs <= 0)
        {
            throw new ConfigValidationException("all request, batch, concurrency, and timeout limits must be positive");
        }

        if (Redis.PoolSize <= 0 || Redis.CacheTtlSecs <= 0)
        {
            throw new ConfigValidationException("Redis pool size and cache TTL must be positive");
        }

        if (string.IsNullOrEmpty(Redis.KeyPrefix) ||
            !Redis.KeyPrefix.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new ConfigValidationException("redis.key_prefix must contain only ASCII letters, digits, '_' or '-'");
        }

        if (Redis.Url is not null && string.IsNullOrWhiteSpace(Redis.Url))
        {
            throw new ConfigValidationException("redis.url cannot be empty when configured");
        }

        if (Postgres.MaxConnections <= 0 || Postgres.ConnectTimeoutSecs <= 0)
        {
            throw new ConfigValidationException("PostgreSQL connection count and timeout must be positive");
        }

        if (Postgres.Url is not null && string.IsNullOrWhiteSpace(Postgres.Url))
        {
            throw new ConfigValidationException("postgres.url cannot be empty when configured");
        }

        foreach (var (name, path) in new[]
                 {
                     ("geoip.city_mmdb_path", Geoip.CityMmdbPath),
                     ("geoip.mmdb_path", Geoip.MmdbPath),
                     ("geoip.asn_mmdb_path", Geoip.AsnMmdbPath),
                     ("geoip.anonymous_ip_mmdb_path", Geoip.AnonymousIpMmdbPath),
                 })
        {
            if (path is not null && string.IsNullOrWhiteSpace(path))
            {
                throw new ConfigValidationException($"{name} cannot be empty when configured");
            }
        }

        if (!Telemetry.MetricsPath.StartsWith('/') ||
            Telemetry.MetricsPath is "/mcp" or "/health" or "/ready")
        {
            throw new ConfigValidationException("telemetry.metrics_path must be a non-reserved absolute path");
        }

        if (Telemetry.OtlpEndpoint is { } endpoint &&
            (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
        {
            throw new ConfigValidationException("telemetry.otlp_endpoint must be an absolute HTTP(S) URL");
        }

        if (TlsFingerprints.MaxAgeSeconds <= 0)
        {
            throw new ConfigValidationException("TLS fingerprint attestation max age must be positive");
        }

        ValidateSecretLength(TlsFingerprints.AttestationKey, "TLS_FINGERPRINT_ATTESTATION_KEY");
        ValidateSecretLength(TlsFingerprints.PreviousAttestationKey, "TLS_FINGERPRINT_ATTESTATION_PREVIOUS_KEY");
        if (TlsFingerprints.KnownBadJa3.Any(value => TlsAttestation.NormalizeJa3(value) is null))
        {
            throw new ConfigValidationException("TLS_KNOWN_BAD_JA3 contains a malformed JA3 digest");
        }

        if (TlsFingerprints.KnownBadJa4.Any(value => TlsAttestation.NormalizeJa4(value) is null))
        {
            throw new ConfigValidationException("TLS_KNOWN_BAD_JA4 contains a malformed JA4 fingerprint");
        }
    }

    private static void ValidateSecretLength(string? secret, string name)
    {
        if (secret is not null && (System.Text.Encoding.UTF8.GetByteCount(secret) < 32 || secret.StartsWith("replace_me", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConfigValidationException($"{name} must contain at least 32 non-placeholder bytes when configured");
        }
    }
}

public sealed class AuthConfig
{
    /// <summary>Bearer tokens accepted by the server.</summary>
    public List<string> Tokens { get; set; } = [];

    /// <summary>If false, every request is treated as caller scope "public".</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Server-side HMAC key (>= 32 bytes) for deriving per-caller cache scopes from bearer
    /// tokens. Leave unset to use a process-local random key.
    /// </summary>
    public string? CacheScopeHmacKey { get; set; }
}

public sealed class LimitsConfig
{
    public int MaxRequestBytes { get; set; } = 1024 * 1024;
    public int MaxBatchSize { get; set; } = 50;
    public int GlobalConcurrency { get; set; } = 256;
    public int PerToolTimeoutSecs { get; set; } = 30;
    public int ClassifyTimeoutSecs { get; set; } = 5;
}

public sealed class RedisConfig
{
    public string? Url { get; set; }
    public int PoolSize { get; set; } = 16;
    public string KeyPrefix { get; set; } = "request_guard";
    public int CacheTtlSecs { get; set; } = 300;
}

public sealed class PostgresConfig
{
    public string? Url { get; set; }
    public int MaxConnections { get; set; } = 10;
    public int ConnectTimeoutSecs { get; set; } = 5;
}

public sealed class GeoipConfig
{
    /// <summary>Backwards-compatible City database path.</summary>
    public string? MmdbPath { get; set; }

    public string? CityMmdbPath { get; set; }
    public string? AsnMmdbPath { get; set; }
    public string? AnonymousIpMmdbPath { get; set; }
}

public sealed class FeatureConfig
{
    public bool EnableBatch { get; set; } = true;
    public bool EnableEnrichment { get; set; } = true;
    public bool EnableFeedback { get; set; } = true;
}

public sealed class TelemetryConfig
{
    public string MetricsPath { get; set; } = "/metrics";
    public string? OtlpEndpoint { get; set; }
    public string ServiceName { get; set; } = "request-guard-mcp";
}

public sealed class TlsFingerprintConfig
{
    public string? AttestationKey { get; set; }
    public string? PreviousAttestationKey { get; set; }
    public int MaxAgeSeconds { get; set; } = 60;
    public List<string> KnownBadJa3 { get; set; } = [];
    public List<string> KnownBadJa4 { get; set; } = [];
}

/// <summary>Thrown when startup configuration fails validation.</summary>
public sealed class ConfigValidationException(string message) : Exception(message);

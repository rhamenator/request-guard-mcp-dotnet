namespace RequestGuardMcp.Core.Configuration;

/// <summary>
/// Top-level application configuration. Ports src/config.rs's <c>Config</c>. Only the sections
/// needed so far are present; Redis/PostgreSQL/GeoIP/feature-flag/TLS-attestation sections are
/// added in the delivery phases that implement those integrations.
/// </summary>
public sealed class AppConfig
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8085;
    public AuthConfig Auth { get; set; } = new();
    public LimitsConfig Limits { get; set; } = new();
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
        if (Auth.Enabled)
        {
            if (Auth.Tokens.Count == 0)
            {
                throw new ConfigValidationException("authentication is enabled but AUTH_TOKENS is empty");
            }

            if (Auth.Tokens.Any(token =>
                    string.IsNullOrEmpty(token) ||
                    token == "replace_me" ||
                    token == "replace_me_with_a_strong_token"))
            {
                throw new ConfigValidationException("AUTH_TOKENS contains an empty or placeholder token");
            }
        }

        if (Auth.CacheScopeHmacKey is { Length: > 0 and < 32 })
        {
            throw new ConfigValidationException("CACHE_SCOPE_HMAC_KEY must contain at least 32 bytes when configured");
        }

        if (Limits.MaxRequestBytes <= 0 ||
            Limits.MaxBatchSize <= 0 ||
            Limits.GlobalConcurrency <= 0 ||
            Limits.PerToolTimeoutSecs <= 0 ||
            Limits.ClassifyTimeoutSecs <= 0)
        {
            throw new ConfigValidationException("all request, batch, concurrency, and timeout limits must be positive");
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

/// <summary>Thrown when startup configuration fails validation.</summary>
public sealed class ConfigValidationException(string message) : Exception(message);

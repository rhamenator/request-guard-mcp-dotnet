using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Tests.Configuration;

public class AppConfigTests
{
    [Fact]
    public void NativeDefaultListenerIsLoopback() => Assert.Equal("127.0.0.1", new AppConfig().Host);

    [Fact]
    public void DefaultConfigWithAuthEnabledAndNoTokensFailsValidation()
    {
        var config = new AppConfig();

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Theory]
    [InlineData("replace_me")]
    [InlineData("replace_me_with_a_strong_token")]
    [InlineData("")]
    public void PlaceholderOrEmptyTokenFailsValidation(string token)
    {
        var config = new AppConfig { Auth = new AuthConfig { Tokens = [token] } };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void RealTokenPassesValidation()
    {
        var config = new AppConfig { Auth = new AuthConfig { Tokens = ["a-real-test-token-with-at-least-32-bytes"] } };

        config.Validate();
    }

    [Fact]
    public void ShortCacheScopeHmacKeyFailsValidation()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Tokens = ["a-real-test-token-with-at-least-32-bytes"], CacheScopeHmacKey = "too-short" },
        };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void LongEnoughCacheScopeHmacKeyPassesValidation()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig
            {
                Tokens = ["a-real-test-token-with-at-least-32-bytes"],
                CacheScopeHmacKey = "0123456789abcdef0123456789abcdef",
            },
        };

        config.Validate();
    }

    [Fact]
    public void LongPlaceholderCacheScopeKeyFailsValidation()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig
            {
                Tokens = ["a-real-test-token-with-at-least-32-bytes"],
                CacheScopeHmacKey = "replace_me_with_32_or_more_random_bytes",
            },
        };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void AuthenticatedPostgresRequiresStableCallerScopeKey()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Tokens = ["a-real-test-token-with-at-least-32-bytes"] },
            Postgres = new PostgresConfig { Url = "Host=localhost;Database=test" },
        };

        var error = Assert.Throws<ConfigValidationException>(config.Validate);
        Assert.Contains("CACHE_SCOPE_HMAC_KEY", error.Message, StringComparison.Ordinal);

        config.Auth.CacheScopeHmacKey = "0123456789abcdef0123456789abcdef";
        config.Validate();
    }

    [Fact]
    public void ZeroConcurrencyLimitFailsValidation()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Enabled = false },
            Limits = new LimitsConfig { GlobalConcurrency = 0 },
        };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void AuthDisabledSkipsTokenValidation()
    {
        var config = new AppConfig { Auth = new AuthConfig { Enabled = false } };

        config.Validate();
    }

    [Theory]
    [InlineData("short-token")]
    [InlineData("prefix-replace_me-and-enough-bytes-123456789")]
    public void ShortOrEmbeddedPlaceholderTokenFailsValidation(string token)
    {
        var config = new AppConfig { Auth = new AuthConfig { Tokens = [token] } };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void AuthenticationCannotBeDisabledOnNonLoopbackListener()
    {
        var config = new AppConfig { Host = "0.0.0.0", Auth = new AuthConfig { Enabled = false } };

        Assert.Throws<ConfigValidationException>(config.Validate);
        config.Validate(stdio: true);
    }

    [Fact]
    public void BrowserOriginAllowlistIsBounded()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig
            {
                Tokens = ["a-real-test-token-with-at-least-32-bytes"],
                AllowedOrigins = Enumerable.Range(0, 33).Select(index => $"https://client-{index}.example").ToList(),
            },
        };

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

    [Fact]
    public void BrowserOriginAllowlistAcceptsBoundary()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig
            {
                Tokens = ["a-real-test-token-with-at-least-32-bytes"],
                AllowedOrigins = Enumerable.Range(0, 32).Select(index => $"https://client-{index}.example").ToList(),
            },
        };

        config.Validate();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmbeddedPlaceholderSecretFailsValidation(bool cacheScopeSecret)
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig
            {
                Tokens = ["a-real-test-token-with-at-least-32-bytes"],
            },
        };
        if (cacheScopeSecret)
        {
            config.Auth.CacheScopeHmacKey = "prefix_replace_me_0123456789abcdef0123456789abcdef";
        }
        else
        {
            config.TlsFingerprints.AttestationKey = "prefix_replace_me_0123456789abcdef0123456789abcdef";
        }

        Assert.Throws<ConfigValidationException>(config.Validate);
    }
    [Theory]
    [InlineData("connections")]
    [InlineData("headers")]
    [InlineData("body")]
    [InlineData("http2_idle")]
    public void ConnectionLimitsMustBePositive(string field)
    {
        var config = new AppConfig { Auth = new AuthConfig { Enabled = false } };
        switch (field)
        {
            case "connections": config.Limits.MaxConnections = 0; break;
            case "headers": config.Limits.HttpHeaderTimeoutSecs = 0; break;
            case "body": config.Limits.HttpBodyTimeoutSecs = 0; break;
            case "http2_idle": config.Limits.Http2IdleTimeoutSecs = 0; break;
        }

        Assert.Throws<ConfigValidationException>(config.Validate);
    }

}

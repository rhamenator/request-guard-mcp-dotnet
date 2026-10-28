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
        var config = new AppConfig { Auth = new AuthConfig { Tokens = ["a-real-test-token"] } };

        config.Validate();
    }

    [Fact]
    public void ShortCacheScopeHmacKeyFailsValidation()
    {
        var config = new AppConfig
        {
            Auth = new AuthConfig { Tokens = ["a-real-test-token"], CacheScopeHmacKey = "too-short" },
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
                Tokens = ["a-real-test-token"],
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
                Tokens = ["a-real-test-token"],
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
            Auth = new AuthConfig { Tokens = ["a-real-test-token"] },
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

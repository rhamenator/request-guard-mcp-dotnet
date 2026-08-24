using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Tests.Configuration;

public class AppConfigTests
{
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
}

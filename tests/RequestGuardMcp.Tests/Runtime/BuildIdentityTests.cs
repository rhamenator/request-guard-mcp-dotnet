using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Core.Util;
using RequestGuardMcp.Host;

namespace RequestGuardMcp.Tests.Runtime;

public class BuildIdentityTests
{
    [Theory]
    [InlineData("0.1.1+abc123def", "0.1.1", "abc123def")]
    [InlineData("0.1.1", "0.1.1", null)]
    [InlineData("0.1.1+", "0.1.1", null)]
    public void InformationalVersionIsSplitIntoVersionAndEmbeddedRevision(string input, string version, string? commit)
    {
        var (actualVersion, actualCommit) = BuildInfo.SplitInformationalVersion(input);
        Assert.Equal(version, actualVersion);
        Assert.Equal(commit, actualCommit);
    }

    [Fact]
    public void CacheDomainDependsOnRevisionAndOverride()
    {
        var tls = new TlsFingerprintConfig();
        var baseline = RequestFingerprint.ComputeCacheDomain(tls, "0.1.1", "aaaa");
        Assert.NotEqual(baseline, RequestFingerprint.ComputeCacheDomain(tls, "0.1.1", "bbbb"));
        Assert.NotEqual(baseline, RequestFingerprint.ComputeCacheDomain(tls, "0.1.1", "aaaa", "team-a"));
        Assert.NotEqual(RequestFingerprint.ComputeCacheDomain(tls, "0.1.1", "unknown", "team-a"),
            RequestFingerprint.ComputeCacheDomain(tls, "0.1.1", "unknown", "team-b"));
    }

    private static BuildInfo Build(string commit) => new("0.1.1", commit, "unknown", ".NET");

    [Fact]
    public void RedisWithAnUnknownRevisionRequiresAnExplicitCacheDomain()
    {
        var config = new AppConfig { Redis = new RedisConfig { Url = "redis://localhost:6379" } };

        Assert.NotNull(CacheIdentityGuard.Problem(config, Build(BuildInfo.UnknownCommit)));

        config.Redis.CacheDomain = "team-a";
        Assert.Null(CacheIdentityGuard.Problem(config, Build(BuildInfo.UnknownCommit)));
    }

    [Fact]
    public void KnownRevisionOrNoRedisNeedsNoOverride()
    {
        var withRedis = new AppConfig { Redis = new RedisConfig { Url = "redis://localhost:6379" } };
        Assert.Null(CacheIdentityGuard.Problem(withRedis, Build("0123456789abcdef")));
        Assert.Null(CacheIdentityGuard.Problem(new AppConfig(), Build(BuildInfo.UnknownCommit)));
    }
}

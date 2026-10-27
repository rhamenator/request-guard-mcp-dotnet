using Microsoft.AspNetCore.Http;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Errors;
using RequestGuardMcp.Mcp.Auth;

namespace RequestGuardMcp.Tests.Auth;

public class OriginPolicyTests
{
    [Fact]
    public void MissingOriginWorksForNonBrowserClients() => OriginPolicy.Validate(new HeaderDictionary(), []);

    [Theory]
    [InlineData("https://client.test")]
    [InlineData("HTTPS://CLIENT.TEST:443")]
    public void ExactConfiguredOriginsAreCanonicalized(string value) =>
        OriginPolicy.Validate(new HeaderDictionary { ["Origin"] = value }, ["https://client.test"]);

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("https://client.test.evil")]
    [InlineData("https://other.test")]
    [InlineData("http://client.test")]
    [InlineData("https://client.test:444")]
    [InlineData("https://user@client.test")]
    [InlineData("https://client.test/")]
    [InlineData("https://client.test/.")]
    [InlineData("https://client.test?x=1")]
    [InlineData("https://client.test https://other.test")]
    public void InvalidOrUnlistedOriginsAreForbidden(string value) =>
        Assert.Throws<AppErrorException>(() => OriginPolicy.Validate(new HeaderDictionary { ["Origin"] = value }, ["https://client.test"]));

    [Fact]
    public void EmptyAllowlistRejectsPresentOrigin() =>
        Assert.Throws<AppErrorException>(() => OriginPolicy.Validate(new HeaderDictionary { ["Origin"] = "https://client.test" }, []));

    [Fact]
    public void RepeatedOriginsAreRejected() =>
        Assert.Throws<AppErrorException>(() => OriginPolicy.Validate(new HeaderDictionary
        {
            ["Origin"] = new Microsoft.Extensions.Primitives.StringValues(["https://client.test", "https://other.test"]),
        }, ["https://client.test"]));

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.test")]
    [InlineData("https://client.test/")]
    [InlineData("https://user@client.test")]
    [InlineData("null")]
    public void InvalidOriginConfigurationFailsStartup(string value) =>
        Assert.Throws<ConfigValidationException>(() => new AppConfig
        {
            Auth = new AuthConfig { Enabled = false, AllowedOrigins = [value] },
        }.Validate());
}

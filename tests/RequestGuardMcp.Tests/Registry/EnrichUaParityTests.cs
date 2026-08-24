using System.Text.Json.Nodes;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Core.Runtime;
using RequestGuardMcp.Tools;

namespace RequestGuardMcp.Tests.Registry;

public sealed class EnrichUaParityTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36", "Chrome", "Windows 10", "pc", false)]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", "Googlebot", "UNKNOWN", "crawler", true)]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)", "misc crawler", "UNKNOWN", "crawler", true)]
    [InlineData("curl/8.0", "HTTP Library", "UNKNOWN", "misc", false)]
    [InlineData("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/120.0 Mobile Safari/537.36", "Chrome", "Android", "smartphone", false)]
    [InlineData("", "UNKNOWN", "UNKNOWN", "UNKNOWN", false)]
    public async Task MatchesWootheeContract(string userAgent, string browser, string os, string deviceType, bool isBot)
    {
        await using var state = new AppState(new AppConfig { Auth = { Enabled = false } });
        var tool = new EnrichUaTool();

        var response = await tool.CallAsync(state, new JsonObject { ["user_agent"] = userAgent }, CancellationToken.None);

        Assert.Equal(browser, response!["browser"]!.GetValue<string>());
        Assert.Equal(os, response["os"]!.GetValue<string>());
        Assert.Equal(deviceType, response["device_type"]!.GetValue<string>());
        Assert.Equal(isBot, response["is_bot"]!.GetValue<bool>());
        Assert.Equal(isBot ? browser : null, response["bot_name"]?.GetValue<string>());
    }

    [Fact]
    public async Task UnknownAgentPreservesRustNullFields()
    {
        await using var state = new AppState(new AppConfig { Auth = { Enabled = false } });
        var response = await new EnrichUaTool().CallAsync(
            state,
            new JsonObject { ["user_agent"] = "entirely-unknown-client" },
            CancellationToken.None);

        Assert.True(response!.AsObject().ContainsKey("browser"));
        Assert.Null(response["browser"]);
        Assert.Null(response["os"]);
        Assert.Null(response["device_type"]);
        Assert.Null(response["bot_name"]);
    }
}

using Microsoft.Extensions.Configuration;
using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Host;

namespace RequestGuardMcp.Tests.Configuration;

public class LegacyTlsOverridesTests
{
    private const string StructuredJa3 = "e7d705a3286e19ea42f587b344ee6865";
    private const string LegacyJa3 = "579ccef312d18482fc42e2b822ca2430";

    private static AppConfig StructuredConfig()
    {
        var values = new Dictionary<string, string?>
        {
            ["TlsFingerprints:KnownBadJa3:0"] = StructuredJa3,
            ["TlsFingerprints:KnownBadJa4:0"] = "t13d1516h2_8daaf6152771_b186095e22b6",
        };
        var config = new AppConfig();
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().Bind(config);
        return config;
    }

    [Fact]
    public void UnsetLegacyVariablesPreserveStructuredLists()
    {
        var config = StructuredConfig();

        LegacyTlsOverrides.Apply(config, null, null);

        Assert.Equal([StructuredJa3], config.TlsFingerprints.KnownBadJa3);
        Assert.Single(config.TlsFingerprints.KnownBadJa4);
    }

    [Fact]
    public void ASetLegacyVariableOverridesOnlyItsOwnList()
    {
        var config = StructuredConfig();

        LegacyTlsOverrides.Apply(config, LegacyJa3, null);

        Assert.Equal([LegacyJa3], config.TlsFingerprints.KnownBadJa3);
        Assert.Single(config.TlsFingerprints.KnownBadJa4);
    }

    [Fact]
    public void AnExplicitlyEmptyLegacyVariableClearsTheList()
    {
        var config = StructuredConfig();

        LegacyTlsOverrides.Apply(config, "", null);

        Assert.Empty(config.TlsFingerprints.KnownBadJa3);
        Assert.Single(config.TlsFingerprints.KnownBadJa4);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", 0)]
    [InlineData(" a , b ,, c ", 3)]
    public void ParseDistinguishesUnsetFromEmpty(string? raw, int? expected) =>
        Assert.Equal(expected, LegacyTlsOverrides.Parse(raw)?.Count);
}

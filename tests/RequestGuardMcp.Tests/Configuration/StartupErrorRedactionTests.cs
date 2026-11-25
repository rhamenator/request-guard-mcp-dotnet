using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Host;

namespace RequestGuardMcp.Tests.Configuration;

public class StartupErrorRedactionTests
{
    private const string Secret = "hunter2-very-secret-value";

    private static string WriteTemp(string extension, string content)
    {
        var path = Path.Join(Path.GetTempPath(), $"rg-config-{Guid.NewGuid():N}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData(".ini", "[postgres]\nurl=ok\nthis line has no assignment " + Secret + "\n", 3)]
    [InlineData(".toml", "[postgres]\nurl = \"ok\"\nthis line has no assignment " + Secret + "\n", 3)]
    [InlineData(".yaml", "postgres:\n  url: ok\nthis line has no colon " + Secret + "\n", 3)]
    public void MalformedConfigurationErrorsNameTheLineButNeverShowIt(string extension, string content, int expectedLine)
    {
        var path = WriteTemp(extension, content);
        try
        {
            var error = Assert.Throws<FormatException>(() => RustConfigFileParser.Parse(path));

            Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
            Assert.Contains($"{expectedLine}", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("connect failed: Host=db;Username=u;Password=" + Secret + ";Database=x", "Password=***")]
    [InlineData("bad url postgres://app:" + Secret + "@db:5432/rg", "postgres://***@db:5432/rg")]
    [InlineData("rejected header Authorization: Bearer " + Secret, "Bearer ***")]
    [InlineData("api_key=" + Secret + " is invalid", "api_key=***")]
    public void ScrubMasksCredentialShapes(string input, string expectedFragment)
    {
        var scrubbed = StartupErrorText.Scrub(input);

        Assert.DoesNotContain(Secret, scrubbed, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, scrubbed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScrubLeavesOrdinaryMessagesAlone()
    {
        const string message = "configuration file 'app.toml' was not found";
        Assert.Equal(message, StartupErrorText.Scrub(message));
        Assert.Equal(string.Empty, StartupErrorText.Scrub(null));
    }
}

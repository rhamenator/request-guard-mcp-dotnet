using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Tests.Configuration;

public sealed class RustConfigFileParserTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"request-guard-config-{Guid.NewGuid():N}");

    public RustConfigFileParserTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("json", "{\"port\":19001,\"auth\":{\"enabled\":false,\"tokens\":[\"one\",\"two\"]},\"limits\":{\"max_request_bytes\":42}}")]
    [InlineData("ini", "port=19001\n[auth]\nenabled=false\ntokens:0=one\ntokens:1=two\n[limits]\nmax_request_bytes=42\n")]
    [InlineData("toml", "port = 19001\n[auth]\nenabled = false\ntokens = [\"one\", \"two\"]\n[limits]\nmax_request_bytes=42\n")]
    [InlineData("yaml", "port: 19001\nauth:\n  enabled: false\n  tokens:\n    - one\n    - two\nlimits:\n  max_request_bytes: 42\n")]
    [InlineData("yml", "port: 19001\nauth:\n  enabled: false\n  tokens: [one, two]\nlimits: {max_request_bytes: 42}\n")]
    [InlineData("json5", "{ port: 19001, auth: { enabled: false, tokens: ['one', 'two'], }, limits: {max_request_bytes: 42} }")]
    [InlineData("ron", "(port: 19001, auth: (enabled: false, tokens: [\"one\", \"two\"],), limits: (max_request_bytes: 42,),)")]
    public void ParseFlattensRustSupportedStructuredFormats(string extension, string content)
    {
        var path = Write($"config.{extension}", content);

        var values = RustConfigFileParser.Parse(path);

        Assert.Equal("19001", values["port"]);
        Assert.Equal("false", values["auth:enabled"]);
        Assert.Equal("one", values["auth:tokens:0"]);
        Assert.Equal("two", values["auth:tokens:1"]);
        Assert.Equal("42", values["limits:maxrequestbytes"]);
    }

    [Fact]
    public void ResolvePathProbesKnownExtensionsLikeRustConfigFileWithName()
    {
        _ = Write("settings.toml", "port = 19002");

        Assert.Equal(Path.Combine(directory, "settings.toml"), RustConfigFileParser.ResolvePath(Path.Combine(directory, "settings")));
    }

    [Fact]
    public void ParseDotEnvMapsMcpSeparatorAndPlainCompatibilityVariables()
    {
        var path = Write(".env", "# comment\nMCP__AUTH__ENABLED=false\nAUTH_TOKENS='alpha,beta'\n");

        var values = RustConfigFileParser.ParseDotEnv(path);

        Assert.Equal("false", values["AUTH:ENABLED"]);
        Assert.Equal("alpha,beta", values["AUTH_TOKENS"]);
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }
}

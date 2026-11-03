using RequestGuardMcp.Core.Configuration;
using RequestGuardMcp.Host;

namespace RequestGuardMcp.Tests.Host;

public class HostingBindingTests
{
    [Fact]
    public void DirectLaunchUsesValidatedRequestGuardAddress()
    {
        var config = new AppConfig { Host = "127.0.0.2", Port = 19091 };

        Assert.Equal("http://127.0.0.2:19091", HostingBinding.DirectListenerUrl(config, null));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData(" 12345 ")]
    public void IisOutOfProcessPreservesAncmPortHandoff(string port)
    {
        var config = new AppConfig { Host = "127.0.0.1", Port = 8085 };

        Assert.Null(HostingBinding.DirectListenerUrl(config, port));
    }
}

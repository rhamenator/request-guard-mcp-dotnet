using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Host;

internal static class HostingBinding
{
    internal static string? DirectListenerUrl(
        AppConfig config,
        string? aspNetCorePort,
        string? aspNetCoreToken) =>
        string.IsNullOrWhiteSpace(aspNetCorePort) || string.IsNullOrWhiteSpace(aspNetCoreToken)
            ? $"http://{config.Host}:{config.Port}"
            : null;
}

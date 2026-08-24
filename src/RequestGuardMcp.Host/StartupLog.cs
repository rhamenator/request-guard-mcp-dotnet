using Microsoft.Extensions.Logging;

namespace RequestGuardMcp.Host;

/// <summary>Source-generated logging (zero-allocation on the hot path) for host startup messages.</summary>
internal static partial class StartupLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "starting MCP server on {Host}:{Port}")]
    public static partial void ServerStarting(ILogger logger, string host, int port);
}

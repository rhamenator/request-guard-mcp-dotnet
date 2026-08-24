namespace RequestGuardMcp.Core.Util;

/// <summary>Ports the RFC3339 helper from src/util/time.rs.</summary>
public static class TimeUtil
{
    public static string NowRfc3339() => DateTimeOffset.UtcNow.ToString("O");
}

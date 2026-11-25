using RequestGuardMcp.Core.Configuration;

namespace RequestGuardMcp.Host;

/// <summary>
/// The sibling Rust project's plain <c>TLS_KNOWN_BAD_JA3</c> / <c>TLS_KNOWN_BAD_JA4</c> variables. They override the
/// structured <c>TlsFingerprints</c> lists only when they are actually set (issue 86); an unset variable must leave
/// values from the configuration file or <c>MCP__</c> variables alone, and an explicitly empty one clears the list.
/// </summary>
internal static class LegacyTlsOverrides
{
    /// <summary>Null when the variable is not set; otherwise the comma-separated entries (an empty value gives an empty list).</summary>
    internal static List<string>? Parse(string? raw) =>
        raw?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    internal static void Apply(AppConfig config, string? rawJa3, string? rawJa4)
    {
        if (Parse(rawJa3) is { } ja3)
        {
            config.TlsFingerprints.KnownBadJa3 = ja3;
        }

        if (Parse(rawJa4) is { } ja4)
        {
            config.TlsFingerprints.KnownBadJa4 = ja4;
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;

namespace RequestGuardMcp.Core.Runtime;

/// <summary>
/// Build/version metadata reported by the <c>model_info</c> tool. Ports src/state.rs's
/// <c>BuildInfo</c>. Unlike the Rust server's compile-time <c>option_env!</c> captures, the git
/// commit and build date are read from environment variables at process start (set by the
/// release/container build, same as the Rust project's own <c>GIT_COMMIT</c>/<c>BUILD_DATE</c>
/// variables) — a deliberate difference since .NET has no equivalent compile-time environment
/// capture without a source generator.
/// </summary>
public sealed record BuildInfo(string Version, string GitCommit, string BuildDate, string RuntimeVersion)
{
    public const string UnknownCommit = "unknown";

    /// <summary>True when no immutable source revision could be determined for this executable.</summary>
    public bool HasUnknownCommit => string.Equals(GitCommit, UnknownCommit, StringComparison.Ordinal);

    public static BuildInfo Current()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.1.2";
        var (version, embeddedCommit) = SplitInformationalVersion(informational);

        return new BuildInfo(
            Version: version,
            // The revision compiled into the assembly (SourceRevisionId) wins: a runtime variable can be missing or wrong,
            // and this value keys the distributed classification cache (issue 80).
            GitCommit: embeddedCommit ?? Environment.GetEnvironmentVariable("GIT_COMMIT") ?? UnknownCommit,
            BuildDate: Environment.GetEnvironmentVariable("BUILD_DATE") ?? "unknown",
            RuntimeVersion: RuntimeInformation.FrameworkDescription);
    }

    /// <summary>Splits "1.2.3+abcdef" (the SDK appends the source revision after '+') into version and revision.</summary>
    public static (string Version, string? Commit) SplitInformationalVersion(string informational)
    {
        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return (informational, null);
        }

        var commit = informational[(plus + 1)..].Trim();
        // An image built without GIT_COMMIT embeds the literal "unknown"; that is the absence of a revision, so the runtime fallback still applies.
        return (informational[..plus], commit.Length == 0 || string.Equals(commit, UnknownCommit, StringComparison.OrdinalIgnoreCase) ? null : commit);
    }
}

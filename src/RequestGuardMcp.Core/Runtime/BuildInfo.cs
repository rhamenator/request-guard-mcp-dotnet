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
    public static BuildInfo Current()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.1.0";

        return new BuildInfo(
            Version: version,
            GitCommit: Environment.GetEnvironmentVariable("GIT_COMMIT") ?? "unknown",
            BuildDate: Environment.GetEnvironmentVariable("BUILD_DATE") ?? "unknown",
            RuntimeVersion: RuntimeInformation.FrameworkDescription);
    }
}

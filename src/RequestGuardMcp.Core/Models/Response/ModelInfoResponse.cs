namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>ModelInfoResponse</c>.</summary>
public sealed record ModelInfoResponse(
    string ModelVersion,
    int ToolCount,
    IReadOnlyList<ToolInfo> Tools,
    BuildInfoResponse BuildInfo);

/// <summary>Ports src/models/response.rs's <c>ToolInfo</c>.</summary>
public sealed record ToolInfo(
    string Name,
    string Description,
    bool Enabled,
    string Version);

/// <summary>
/// Ports src/models/response.rs's <c>BuildInfoResponse</c>. The Rust field <c>rust_version</c>
/// (the compiler/toolchain version) has no direct .NET analogue; this port reports the .NET
/// runtime version under <c>runtime_version</c> instead — a deliberate wire-level difference,
/// since this metadata is informational rather than part of a published tool schema.
/// </summary>
public sealed record BuildInfoResponse(
    string Version,
    string GitCommit,
    string BuildDate,
    string RuntimeVersion);

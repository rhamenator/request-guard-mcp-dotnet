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
/// Ports src/models/response.rs's <c>BuildInfoResponse</c>. The legacy wire field remains named
/// <c>rust_version</c> for compatibility; in this implementation its value identifies the .NET
/// runtime. Consumers must treat the value as language-specific build metadata.
/// </summary>
public sealed record BuildInfoResponse(
    string Version,
    string GitCommit,
    string BuildDate,
    string RustVersion);

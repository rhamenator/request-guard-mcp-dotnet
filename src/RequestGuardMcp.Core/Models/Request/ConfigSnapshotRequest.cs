namespace RequestGuardMcp.Core.Models.Request;

/// <summary>Ports src/models/request.rs's <c>ConfigSnapshotRequest</c>.</summary>
public sealed class ConfigSnapshotRequest
{
    public bool? RedactSecrets { get; set; }
}

using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>ConfigSnapshotResponse</c>.</summary>
public sealed record ConfigSnapshotResponse(JsonNode? Snapshot, string GeneratedAt);

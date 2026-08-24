namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>WarmupResponse</c>.</summary>
public sealed record WarmupResponse(IReadOnlyList<string> Warmed, IReadOnlyList<string> Skipped, ulong LatencyMs);

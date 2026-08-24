namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>HealthResponse</c>.</summary>
public sealed record HealthResponse(
    string Status,
    string Version,
    ulong UptimeSeconds,
    IReadOnlyDictionary<string, ComponentHealth> Checks);

/// <summary>Ports src/models/response.rs's <c>ComponentHealth</c>.</summary>
public sealed record ComponentHealth(
    string Status,
    string? Message,
    ulong? LatencyMs);

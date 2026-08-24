namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>SelfTestResponse</c>.</summary>
public sealed record SelfTestResponse(
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<SelfTestResult> Results,
    string OverallStatus);

/// <summary>Ports src/models/response.rs's <c>SelfTestResult</c>.</summary>
public sealed record SelfTestResult(string Name, bool Passed, string? Message, ulong LatencyMs);

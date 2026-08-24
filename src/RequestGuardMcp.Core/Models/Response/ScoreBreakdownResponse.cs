namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>ScoreBreakdownResponse</c>.</summary>
public sealed record ScoreBreakdownResponse(
    string? RequestId,
    double TotalScore,
    IReadOnlyList<ScoreComponent> Breakdown,
    double ThresholdAllow,
    double ThresholdBlock);

/// <summary>Ports src/models/response.rs's <c>ScoreComponent</c>.</summary>
public sealed record ScoreComponent(string Engine, double Score, double Weight, double WeightedScore, IReadOnlyList<string> Signals);

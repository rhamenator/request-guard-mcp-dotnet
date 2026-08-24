namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>ExplainResponse</c>.</summary>
public sealed record ExplainResponse(
    string RequestId,
    string Explanation,
    IReadOnlyList<ExplanationFactor> Factors,
    IReadOnlyList<string> Recommendations);

/// <summary>Ports src/models/response.rs's <c>ExplanationFactor</c>.</summary>
public sealed record ExplanationFactor(string Name, double Contribution, string Description);

namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>BatchClassifyResponse</c>.</summary>
public sealed record BatchClassifyResponse(
    IReadOnlyList<BatchItemResult> Results,
    int Total,
    int Processed,
    int Errors,
    ulong LatencyMs);

/// <summary>Ports src/models/response.rs's <c>BatchItemResult</c>.</summary>
public sealed record BatchItemResult(int Index, ClassifyResponse? Result, string? Error);

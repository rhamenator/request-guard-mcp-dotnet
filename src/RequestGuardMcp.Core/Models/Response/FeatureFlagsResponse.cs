namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>FeatureFlagsResponse</c>.</summary>
public sealed record FeatureFlagsResponse(IReadOnlyDictionary<string, bool> Flags);

namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>ValidatePayloadResponse</c>.</summary>
public sealed record ValidatePayloadResponse(bool Valid, IReadOnlyList<ValidationError> Errors);

/// <summary>Ports src/models/response.rs's <c>ValidationError</c>.</summary>
public sealed record ValidationError(string Path, string Message);

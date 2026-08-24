using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Models.Response;

/// <summary>Ports src/models/response.rs's <c>RedactPreviewResponse</c>.</summary>
public sealed record RedactPreviewResponse(JsonNode? Redacted, IReadOnlyList<string> FieldsRedacted);

using System.Text.Json;

namespace RequestGuardMcp.Core.Json;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used for every MCP wire message and
/// tool payload. Property names are snake_case to match the Rust server's serde field names, so
/// this port stays wire-compatible with existing MCP clients.
/// </summary>
public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

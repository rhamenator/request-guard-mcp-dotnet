using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using RequestGuardMcp.Core.Errors;

namespace RequestGuardMcp.Core.Json;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used for every MCP wire message and
/// tool payload. Property names and enum values are snake_case to match the Rust server's serde
/// field/variant names, so this port stays wire-compatible with existing MCP clients.
/// </summary>
public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Deserializes tool params, or a default instance when params were omitted. Ports
    /// the <c>params.map(...).unwrap_or(default)</c> pattern used throughout
    /// src/mcp/tool_registry.rs.</summary>
    public static T ParamsOrDefault<T>(JsonNode? node) where T : new()
    {
        if (node is null)
        {
            return new T();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(node, Options) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppErrorException.InvalidRequest(ex.Message);
        }
    }

    /// <summary>Deserializes required tool params, throwing <see cref="AppErrorException.InvalidRequest"/>
    /// when absent or malformed. Ports src/mcp/tool_registry.rs's <c>required_params</c>.</summary>
    public static T RequiredParams<T>(JsonNode? node)
    {
        if (node is null)
        {
            throw AppErrorException.InvalidRequest("params required");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(node, Options) ?? throw AppErrorException.InvalidRequest("params required");
        }
        catch (JsonException ex)
        {
            throw AppErrorException.InvalidRequest(ex.Message);
        }
    }
}

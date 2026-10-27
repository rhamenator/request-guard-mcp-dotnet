using System.Text.Json.Nodes;

namespace RequestGuardMcp.Mcp.Registry;

internal static class ToolSchemas
{
    private static readonly JsonObject Schemas = Load();

    public static JsonNode For(string name) => Schemas[name]?.DeepClone() ?? new JsonObject { ["type"] = "object" };

    private static JsonObject Load()
    {
        using var stream = typeof(ToolSchemas).Assembly.GetManifestResourceStream("RequestGuardMcp.Mcp.Registry.tool-schemas.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }
}

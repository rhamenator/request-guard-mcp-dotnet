using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Json;

/// <summary>Recursively redacts matching field names in a JSON value. Ports src/util/json.rs's <c>redact_fields</c>.</summary>
public static class JsonRedaction
{
    public const string RedactedPlaceholder = "[REDACTED]";

    public static void RedactFields(JsonNode? value, IReadOnlyList<string> fields)
    {
        switch (value)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(pair => pair.Key).ToList())
                {
                    if (fields.Any(field => string.Equals(field, key, StringComparison.OrdinalIgnoreCase)))
                    {
                        obj[key] = RedactedPlaceholder;
                    }
                    else
                    {
                        RedactFields(obj[key], fields);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    RedactFields(item, fields);
                }

                break;
        }
    }
}

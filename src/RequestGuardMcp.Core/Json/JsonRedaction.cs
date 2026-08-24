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

    public static IReadOnlyList<string> FindPresentFields(JsonNode? value, IReadOnlyList<string> fields)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Find(value, fields, present);
        return fields.Where(present.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Find(JsonNode? value, IReadOnlyList<string> fields, ISet<string> present)
    {
        switch (value)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    var requested = fields.FirstOrDefault(field => string.Equals(field, key, StringComparison.OrdinalIgnoreCase));
                    if (requested is not null)
                    {
                        present.Add(requested);
                    }

                    Find(child, fields, present);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    Find(child, fields, present);
                }

                break;
        }
    }
}

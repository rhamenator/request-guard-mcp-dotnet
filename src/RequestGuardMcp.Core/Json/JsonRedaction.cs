using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Json;

/// <summary>Recursively redacts matching field names in a JSON value. Ports src/util/json.rs's <c>redact_fields</c>.</summary>
public static class JsonRedaction
{
    public const string RedactedPlaceholder = "[REDACTED]";
    public const int MaxFields = 256;
    public const int MaxFieldNameLength = 256;
    public const int MaxDepth = 64;
    public const int MaxNodes = 10_000;

    public static void RedactFields(JsonNode? value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        var fieldSet = BuildFieldSet(fields);
        if (fieldSet.Count == 0 || value is null)
        {
            return;
        }

        var nodes = 0;
        Traverse(value, fieldSet, present: null, redact: true, depth: 0, ref nodes, cancellationToken);
    }

    public static IReadOnlyList<string> FindPresentFields(JsonNode? value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        var fieldSet = BuildFieldSet(fields);
        if (fieldSet.Count == 0 || value is null)
        {
            return [];
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = 0;
        Traverse(value, fieldSet, present, redact: false, depth: 0, ref nodes, cancellationToken);
        return OrderPresentFields(fields, present);
    }

    public static IReadOnlyList<string> RedactAndCollectPresentFields(JsonNode? value, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        var fieldSet = BuildFieldSet(fields);
        if (fieldSet.Count == 0 || value is null)
        {
            return [];
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = 0;
        Traverse(value, fieldSet, present, redact: true, depth: 0, ref nodes, cancellationToken);
        return OrderPresentFields(fields, present);
    }

    private static HashSet<string> BuildFieldSet(IReadOnlyList<string> fields)
    {
        if (fields.Count > MaxFields)
        {
            throw new ArgumentException($"Too many redaction fields (max {MaxFields}).", nameof(fields));
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (field.Length > MaxFieldNameLength)
            {
                throw new ArgumentException($"Redaction field name exceeds max length ({MaxFieldNameLength}).", nameof(fields));
            }

            set.Add(field);
        }

        return set;
    }

    private static List<string> OrderPresentFields(IReadOnlyList<string> fields, HashSet<string> present)
    {
        if (present.Count == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>(present.Count);
        foreach (var field in fields)
        {
            if (present.Contains(field) && seen.Add(field))
            {
                ordered.Add(field);
            }
        }

        return ordered;
    }

    private static void Traverse(
        JsonNode? value,
        HashSet<string> fieldSet,
        HashSet<string>? present,
        bool redact,
        int depth,
        ref int nodes,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaxDepth)
        {
            throw new ArgumentException($"Payload exceeds maximum redaction depth ({MaxDepth}).");
        }

        if (++nodes > MaxNodes)
        {
            throw new ArgumentException($"Payload exceeds maximum redaction node count ({MaxNodes}).");
        }

        switch (value)
        {
            case JsonObject obj:
                var keys = redact ? obj.Select(pair => pair.Key).ToList() : null;
                if (keys is not null)
                {
                    foreach (var key in keys)
                    {
                        if (fieldSet.Contains(key))
                        {
                            present?.Add(key);
                            obj[key] = RedactedPlaceholder;
                        }
                        else
                        {
                            Traverse(obj[key], fieldSet, present, redact, depth + 1, ref nodes, cancellationToken);
                        }
                    }
                }
                else
                {
                    foreach (var (key, child) in obj)
                    {
                        if (fieldSet.Contains(key))
                        {
                            present?.Add(key);
                        }

                        Traverse(child, fieldSet, present, redact: false, depth + 1, ref nodes, cancellationToken);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    Traverse(item, fieldSet, present, redact, depth + 1, ref nodes, cancellationToken);
                }

                break;
        }
    }
}

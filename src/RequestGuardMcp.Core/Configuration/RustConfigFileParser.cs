using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RequestGuardMcp.Core.Configuration;

/// <summary>
/// Loads the non-JSON configuration formats enabled by the Rust <c>config</c> crate. The parser
/// intentionally targets configuration data (nested mappings, scalar values, and scalar arrays),
/// not the complete general-purpose TOML/YAML/JSON5/RON language surface.
/// </summary>
public static class RustConfigFileParser
{
    private static readonly string[] SupportedExtensions =
        [".toml", ".json", ".yaml", ".yml", ".ini", ".ron", ".json5"];

    public static string ResolvePath(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        var fullPath = Path.GetFullPath(configuredPath);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (Path.GetExtension(fullPath).Length == 0)
        {
            foreach (var extension in SupportedExtensions)
            {
                if (File.Exists(fullPath + extension))
                {
                    return fullPath + extension;
                }
            }
        }

        throw new FileNotFoundException($"configuration file '{configuredPath}' was not found", fullPath);
    }

    public static IReadOnlyDictionary<string, string?> Parse(string path)
    {
        var resolvedPath = ResolvePath(path);
        var text = File.ReadAllText(resolvedPath, Encoding.UTF8);
        return Path.GetExtension(resolvedPath).ToLowerInvariant() switch
        {
            ".json" => ParseJson(text),
            ".ini" => ParseIni(text),
            ".toml" => ParseToml(text),
            ".yaml" or ".yml" => ParseYaml(text),
            ".ron" or ".json5" => Flatten(new StructuredValueParser(text).Parse()),
            _ => throw new NotSupportedException(
                $"CONFIG_FILE extension '{Path.GetExtension(resolvedPath)}' is not supported"),
        };
    }

    public static IReadOnlyDictionary<string, string?> ReadMcpEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var key = variable.Key.ToString();
            if (key is null || !key.StartsWith("MCP__", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[NormalizeConfigurationPath(key[5..].Replace("__", ":", StringComparison.Ordinal))] =
                variable.Value?.ToString();
        }

        return result;
    }

    /// <summary>Parses the subset of dotenv syntax used by the Rust <c>dotenvy</c> loader.</summary>
    public static IReadOnlyDictionary<string, string?> ParseDotEnv(string path)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return result;
        }

        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = StripComment(rawLine, '#').Trim();
            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line[7..].TrimStart();
            }

            var equals = FindUnquoted(line, '=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = Unquote(line[(equals + 1)..].Trim());
            var configurationKey = key.StartsWith("MCP__", StringComparison.OrdinalIgnoreCase)
                ? key[5..].Replace("__", ":", StringComparison.Ordinal)
                : key;
            result[key.StartsWith("MCP__", StringComparison.OrdinalIgnoreCase)
                ? NormalizeConfigurationPath(configurationKey)
                : configurationKey] = value;
        }

        return result;
    }

    private static Dictionary<string, string?> ParseJson(string text)
    {
        var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        }) ?? throw new FormatException("the JSON configuration cannot be null");
        if (root is not JsonObject mapping)
        {
            throw new FormatException("the configuration root must be a mapping");
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in mapping)
        {
            FlattenJsonValue(value, key, result);
        }

        return result;
    }

    private static Dictionary<string, string?> ParseIni(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = StripComment(StripComment(rawLine, ';'), '#').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            var equals = FindUnquoted(line, '=');
            if (equals <= 0)
            {
                throw new FormatException($"invalid INI assignment: {line}");
            }

            var key = line[..equals].Trim();
            var path = section.Length == 0 ? key : $"{section}:{key}";
            result[NormalizeConfigurationPath(path)] = Unquote(line[(equals + 1)..].Trim());
        }

        return result;
    }

    private static Dictionary<string, string?> ParseToml(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var section = new List<string>();
        foreach (var rawLine in LogicalTomlLines(text))
        {
            var line = StripComment(rawLine, '#').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = SplitDottedKey(line[1..^1]);
                continue;
            }

            var equals = FindUnquoted(line, '=');
            if (equals <= 0)
            {
                throw new FormatException($"invalid TOML assignment: {line}");
            }

            var path = section.Concat(SplitDottedKey(line[..equals])).ToArray();
            var value = new StructuredValueParser(line[(equals + 1)..]).ParseValueOnly();
            FlattenValue(value, string.Join(':', path), result);
        }

        return result;
    }

    private static IEnumerable<string> LogicalTomlLines(string text)
    {
        var current = new StringBuilder();
        var depth = 0;
        char quote = '\0';
        var escaped = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(line);
            foreach (var character in StripComment(line, '#'))
            {
                if (quote != '\0')
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\' && quote == '"')
                    {
                        escaped = true;
                    }
                    else if (character == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    quote = character;
                }
                else if (character is '[' or '{')
                {
                    depth++;
                }
                else if (character is ']' or '}')
                {
                    depth--;
                }
            }

            if (depth <= 0 && quote == '\0')
            {
                yield return current.ToString();
                current.Clear();
                depth = 0;
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static Dictionary<string, string?> ParseYaml(string text)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var parents = new Stack<(int Indent, string Path)>();
        var sequenceIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (rawLine.Trim() is "" or "---" or "...")
            {
                continue;
            }

            var indent = rawLine.TakeWhile(char.IsWhiteSpace).Count();
            var line = StripComment(rawLine[indent..], '#').TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            while (parents.Count > 0 && indent <= parents.Peek().Indent)
            {
                parents.Pop();
            }

            var parent = parents.Count == 0 ? string.Empty : parents.Peek().Path;
            if (line.StartsWith("- ", StringComparison.Ordinal) || line == "-")
            {
                if (parent.Length == 0)
                {
                    throw new FormatException("a top-level YAML sequence is not a valid application config");
                }

                var index = sequenceIndexes.GetValueOrDefault(parent);
                sequenceIndexes[parent] = index + 1;
                var itemPath = $"{parent}:{index}";
                var item = line.Length == 1 ? string.Empty : line[2..].Trim();
                if (item.Length == 0)
                {
                    parents.Push((indent, itemPath));
                }
                else
                {
                    FlattenValue(ParseYamlScalar(item), itemPath, result);
                }

                continue;
            }

            var colon = FindUnquoted(line, ':');
            if (colon <= 0)
            {
                throw new FormatException($"invalid YAML mapping: {line}");
            }

            var key = Unquote(line[..colon].Trim());
            var path = parent.Length == 0 ? key : $"{parent}:{key}";
            var valueText = line[(colon + 1)..].Trim();
            if (valueText.Length == 0)
            {
                parents.Push((indent, path));
            }
            else
            {
                FlattenValue(ParseYamlScalar(valueText), path, result);
            }
        }

        return result;
    }

    private static object? ParseYamlScalar(string value)
    {
        if (value.StartsWith('[') || value.StartsWith('{') || value.StartsWith('"') || value.StartsWith('\''))
        {
            return new StructuredValueParser(value).ParseValueOnly();
        }

        return value.ToLowerInvariant() switch
        {
            "null" or "~" => null,
            "true" => true,
            "false" => false,
            _ => value,
        };
    }

    private static Dictionary<string, string?> Flatten(object? root)
    {
        if (root is not Dictionary<string, object?> mapping)
        {
            throw new FormatException("the configuration root must be a mapping");
        }

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in mapping)
        {
            FlattenValue(value, key, result);
        }

        return result;
    }

    private static void FlattenValue(object? value, string path, IDictionary<string, string?> output)
    {
        switch (value)
        {
            case Dictionary<string, object?> mapping:
                foreach (var (key, child) in mapping)
                {
                    FlattenValue(child, $"{path}:{key}", output);
                }

                break;
            case List<object?> sequence:
                for (var index = 0; index < sequence.Count; index++)
                {
                    FlattenValue(sequence[index], $"{path}:{index}", output);
                }

                break;
            case bool boolean:
                output[NormalizeConfigurationPath(path)] = boolean ? "true" : "false";
                break;
            case null:
                output[NormalizeConfigurationPath(path)] = null;
                break;
            case IFormattable formattable:
                output[NormalizeConfigurationPath(path)] = formattable.ToString(null, CultureInfo.InvariantCulture);
                break;
            default:
                output[NormalizeConfigurationPath(path)] = value.ToString();
                break;
        }
    }

    private static void FlattenJsonValue(JsonNode? value, string path, IDictionary<string, string?> output)
    {
        switch (value)
        {
            case JsonObject mapping:
                foreach (var (key, child) in mapping)
                {
                    FlattenJsonValue(child, $"{path}:{key}", output);
                }

                break;
            case JsonArray sequence:
                for (var index = 0; index < sequence.Count; index++)
                {
                    FlattenJsonValue(sequence[index], $"{path}:{index}", output);
                }

                break;
            case null:
                output[NormalizeConfigurationPath(path)] = null;
                break;
            case JsonValue scalar when scalar.TryGetValue<string>(out var text):
                output[NormalizeConfigurationPath(path)] = text;
                break;
            default:
                output[NormalizeConfigurationPath(path)] = value.ToJsonString();
                break;
        }
    }

    private static string NormalizeConfigurationPath(string path) =>
        path.Replace("_", string.Empty, StringComparison.Ordinal);

    private static List<string> SplitDottedKey(string key) =>
        key.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Unquote)
            .ToList();

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
        {
            return (string?)new StructuredValueParser(value).ParseValueOnly() ?? string.Empty;
        }

        return value;
    }

    private static int FindUnquoted(string value, char sought)
    {
        char quote = '\0';
        var escaped = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\' && quote == '"')
                {
                    escaped = true;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == sought)
            {
                return index;
            }
        }

        return -1;
    }

    private static string StripComment(string value, char marker)
    {
        var index = FindUnquoted(value, marker);
        return index < 0 ? value : value[..index];
    }

    private sealed class StructuredValueParser(string text)
    {
        private int position;

        public object? Parse()
        {
            SkipTrivia();
            var value = ParseValue();
            SkipTrivia();
            if (position != text.Length)
            {
                throw Error("unexpected trailing content");
            }

            return value;
        }

        public object? ParseValueOnly() => Parse();

        private object? ParseValue()
        {
            SkipTrivia();
            if (position >= text.Length)
            {
                throw Error("expected a value");
            }

            return text[position] switch
            {
                '{' => ParseMapping('{', '}'),
                '(' => ParseMapping('(', ')'),
                '[' => ParseSequence(),
                '"' or '\'' => ParseString(),
                _ => ParseBareValue(),
            };
        }

        private Dictionary<string, object?> ParseMapping(char open, char close)
        {
            Expect(open);
            var mapping = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            SkipTrivia();
            while (!TryConsume(close))
            {
                var key = text[position] is '"' or '\'' ? ParseString() : ParseIdentifier();
                SkipTrivia();
                if (!TryConsume(':') && !TryConsume('='))
                {
                    throw Error("expected ':' after a mapping key");
                }

                mapping[key] = ParseValue();
                SkipTrivia();
                if (TryConsume(close))
                {
                    break;
                }

                Expect(',');
                SkipTrivia();
            }

            return mapping;
        }

        private List<object?> ParseSequence()
        {
            Expect('[');
            var sequence = new List<object?>();
            SkipTrivia();
            while (!TryConsume(']'))
            {
                sequence.Add(ParseValue());
                SkipTrivia();
                if (TryConsume(']'))
                {
                    break;
                }

                Expect(',');
                SkipTrivia();
            }

            return sequence;
        }

        private object? ParseBareValue()
        {
            var token = ParseIdentifier(allowNumberPunctuation: true);
            switch (token.ToLowerInvariant())
            {
                case "true": return true;
                case "false": return false;
                case "null":
                case "none": return null;
                case "some":
                    SkipTrivia();
                    Expect('(');
                    var optional = ParseValue();
                    SkipTrivia();
                    Expect(')');
                    return optional;
            }

            if (long.TryParse(token.Replace("_", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                return integer;
            }

            if (double.TryParse(token.Replace("_", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var floatingPoint))
            {
                return floatingPoint;
            }

            return token;
        }

        private string ParseIdentifier(bool allowNumberPunctuation = false)
        {
            SkipTrivia();
            var start = position;
            while (position < text.Length)
            {
                var character = text[position];
                if (char.IsLetterOrDigit(character) || character is '_' or '-' ||
                    (allowNumberPunctuation && character is '+' or '.'))
                {
                    position++;
                    continue;
                }

                break;
            }

            if (start == position)
            {
                throw Error("expected an identifier");
            }

            return text[start..position];
        }

        private string ParseString()
        {
            var quote = text[position++];
            var result = new StringBuilder();
            while (position < text.Length)
            {
                var character = text[position++];
                if (character == quote)
                {
                    return result.ToString();
                }

                if (character != '\\' || quote == '\'')
                {
                    result.Append(character);
                    continue;
                }

                if (position >= text.Length)
                {
                    throw Error("unterminated escape sequence");
                }

                var escaped = text[position++];
                result.Append(escaped switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'b' => '\b',
                    'f' => '\f',
                    '\\' => '\\',
                    '/' => '/',
                    '"' => '"',
                    '\'' => '\'',
                    'u' => ParseUnicodeEscape(),
                    _ => escaped,
                });
            }

            throw Error("unterminated string");
        }

        private char ParseUnicodeEscape()
        {
            if (position + 4 > text.Length ||
                !ushort.TryParse(text.AsSpan(position, 4), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out var value))
            {
                throw Error("invalid Unicode escape");
            }

            position += 4;
            return (char)value;
        }

        private void SkipTrivia()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position]))
                {
                    position++;
                }
                else if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/')
                {
                    position += 2;
                    while (position < text.Length && text[position] != '\n')
                    {
                        position++;
                    }
                }
                else if (position + 1 < text.Length && text[position] == '/' && text[position + 1] == '*')
                {
                    var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        throw Error("unterminated block comment");
                    }

                    position = end + 2;
                }
                else if (text[position] == '#')
                {
                    while (position < text.Length && text[position] != '\n')
                    {
                        position++;
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private bool TryConsume(char expected)
        {
            SkipTrivia();
            if (position < text.Length && text[position] == expected)
            {
                position++;
                return true;
            }

            return false;
        }

        private void Expect(char expected)
        {
            if (!TryConsume(expected))
            {
                throw Error($"expected '{expected}'");
            }
        }

        private FormatException Error(string message) =>
            new($"{message} at character {position.ToString(CultureInfo.InvariantCulture)}");
    }
}

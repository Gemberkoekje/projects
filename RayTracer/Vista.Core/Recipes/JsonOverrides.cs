using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vista.Recipes;

/// <summary>
/// Field overrides for Vista JSON files, so a contact sheet (vista-plan §7.4) can vary any recipe or camera
/// field without knowing the schema: an override sets one property by its dotted camelCase path, and the
/// result still goes through the strict parser, so a misspelt path or a wrong value fails loudly.
/// </summary>
public static class JsonOverrides
{
    /// <summary>
    /// Returns <paramref name="json"/> with each override applied in path order: the property at the dotted
    /// path (e.g. <c>time</c>, <c>position.y</c>) is set to the override's JSON value, creating missing parent
    /// objects. With no overrides the text is returned unchanged.
    /// </summary>
    /// <exception cref="InvalidDataException">A path is malformed or runs through a value that is not an object.</exception>
    public static string Apply(string json, IReadOnlyDictionary<string, string> overrides)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(overrides);
        if (overrides.Count == 0)
            return json;

        JsonObject root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException("Overrides need a JSON object at the top level.");
        foreach ((string path, string value) in overrides.OrderBy(o => o.Key, StringComparer.Ordinal))
        {
            string[] segments = SplitPath(path);
            JsonObject parent = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                switch (parent[segments[i]])
                {
                    case JsonObject child:
                        parent = child;
                        break;
                    case null:
                        var created = new JsonObject();
                        parent[segments[i]] = created;
                        parent = created;
                        break;
                    default:
                        throw new InvalidDataException($"Cannot set '{path}': '{string.Join('.', segments[..(i + 1)])}' is not an object.");
                }
            }
            parent[segments[^1]] = JsonNode.Parse(value);
        }
        return root.ToJsonString();
    }

    /// <summary>
    /// Reads a value written loosely on a command line and returns it as JSON text: numbers, <c>true</c>,
    /// <c>false</c>, <c>null</c>, quoted strings and objects are JSON as written; a bare word is a string
    /// (<c>goldenHour</c>); <c>[a, b]</c> is an array of loose values (<c>[snowCappedMountains, lake]</c>).
    /// </summary>
    /// <exception cref="FormatException">The text is empty, or a quoted string or object is malformed.</exception>
    public static string LooseValue(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string t = text.Trim();
        if (t.Length == 0)
            throw new FormatException("An empty value.");
        if (t[0] == '[')
        {
            if (t[^1] != ']')
                throw new FormatException($"Unclosed list '{t}'.");
            string inner = t[1..^1];
            IEnumerable<string> items = inner.Trim().Length == 0 ? [] : SplitList(inner).Select(LooseValue);
            return "[" + string.Join(",", items) + "]";
        }
        if (t[0] is '"' or '{')
        {
            try
            {
                return JsonNode.Parse(t)!.ToJsonString();
            }
            catch (JsonException e)
            {
                throw new FormatException($"Malformed JSON value '{t}': {e.Message}", e);
            }
        }
        if (t is "true" or "false" or "null")
            return t;
        try
        {
            if (JsonNode.Parse(t) is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
                return number.ToJsonString();
        }
        catch (JsonException)
        {
            // Not a number: a bare word.
        }
        return JsonSerializer.Serialize(t);
    }

    /// <summary>
    /// Splits a comma-separated list at its top level: commas inside brackets, braces or double quotes do not
    /// split. Items are trimmed; an empty item is an error.
    /// </summary>
    /// <exception cref="FormatException">An item is empty, or brackets or quotes are unbalanced.</exception>
    public static IReadOnlyList<string> SplitList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var items = new List<string>();
        var current = new StringBuilder();
        int depth = 0;
        bool quoted = false;
        bool escaped = false;
        foreach (char c in text)
        {
            if (quoted)
            {
                current.Append(c);
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    quoted = false;
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case '[' or '{':
                    depth++;
                    break;
                case ']' or '}':
                    if (--depth < 0)
                        throw new FormatException($"Unbalanced '{c}' in '{text}'.");
                    break;
                case ',' when depth == 0:
                    items.Add(Item(current, text));
                    current.Clear();
                    continue;
            }
            current.Append(c);
        }
        if (quoted || depth != 0)
            throw new FormatException($"Unbalanced brackets or quotes in '{text}'.");
        items.Add(Item(current, text));
        return items;
    }

    private static string Item(StringBuilder current, string text)
    {
        string item = current.ToString().Trim();
        return item.Length > 0 ? item : throw new FormatException($"An empty item in '{text}'.");
    }

    private static string[] SplitPath(string path)
    {
        string[] segments = path.Split('.');
        if (segments.Any(s => s.Length == 0))
            throw new InvalidDataException($"Malformed field path '{path}'.");
        return segments;
    }
}

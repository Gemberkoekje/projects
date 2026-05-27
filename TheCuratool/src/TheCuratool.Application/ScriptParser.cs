using System.IO;
using System.Text.Json;

using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// Parses Blood on the Clocktower script JSON into a <see cref="ScriptParseResult"/>.
/// Supports both raw JSON strings and streams. Unknown character IDs are resolved
/// via <see cref="CharacterDatabase.Resolve"/> with graceful fallback.
/// </summary>
public sealed class ScriptParser
{
    /// <summary>
    /// Parses a script from a raw JSON string.
    /// </summary>
    /// <param name="json">The raw JSON content of the script file.</param>
    /// <param name="characterDatabase">Database used to resolve character definitions by ID.</param>
    public ScriptParseResult Parse(string json, CharacterDatabase characterDatabase)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            var emptyScript = new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>());
            return new ScriptParseResult(emptyScript, Array.Empty<string>(), new[] { "Script JSON content is required." });
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ParseDocument(document, characterDatabase);
        }
        catch (JsonException ex)
        {
            var emptyScript = new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>());
            return new ScriptParseResult(emptyScript, Array.Empty<string>(), new[] { $"Invalid JSON: {ex.Message}" });
        }
    }

    /// <summary>
    /// Parses a script from a readable <see cref="Stream"/> containing JSON.
    /// </summary>
    /// <param name="stream">A readable stream containing the script JSON.</param>
    /// <param name="characterDatabase">Database used to resolve character definitions by ID.</param>
    public ScriptParseResult Parse(Stream stream, CharacterDatabase characterDatabase)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return Parse(json, characterDatabase);
    }

    private static ScriptParseResult ParseDocument(JsonDocument document, CharacterDatabase characterDatabase)
    {
        ArgumentNullException.ThrowIfNull(characterDatabase);

        var warnings = new List<string>();
        var errors = new List<string>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            var emptyScript = new Script(string.Empty, string.Empty, Array.Empty<CharacterDefinition>());
            errors.Add("Script JSON root must be an array.");
            return new ScriptParseResult(emptyScript, warnings.AsReadOnly(), errors.AsReadOnly());
        }

        var name = string.Empty;
        var author = string.Empty;
        var characterIds = new List<string>();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var id = element.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                characterIds.Add(id.Trim());
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Script entries must be either character id strings or objects containing an id.");
                continue;
            }

            var entryId = ReadStringOrDefault(element, "id", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(entryId))
            {
                errors.Add("Object script entries must include a non-empty id.");
                continue;
            }

            if (string.Equals(entryId, "_meta", StringComparison.OrdinalIgnoreCase))
            {
                name = ReadStringOrDefault(element, "name", name);
                author = ReadStringOrDefault(element, "author", author);
                continue;
            }

            characterIds.Add(entryId);
        }

        var characters = characterIds
            .Select(characterDatabase.Resolve)
            .ToList()
            .AsReadOnly();

        var script = new Script(name, author, characters);
        Validate(script, warnings, errors);

        return new ScriptParseResult(script, warnings.AsReadOnly(), errors.AsReadOnly());
    }

    private static void Validate(Script script, List<string> warnings, List<string> errors)
    {
        if (!script.Characters.Any(c => c.Type == CharacterType.Townsfolk))
        {
            errors.Add("Script must include at least one Townsfolk character.");
        }

        if (!script.Characters.Any(c => c.Type == CharacterType.Demon))
        {
            warnings.Add("Script has no Demon characters.");
        }

        if (!script.Characters.Any(c => c.Type == CharacterType.Outsider))
        {
            warnings.Add("Script has no Outsider characters.");
        }

        if (!script.Characters.Any(c => c.Type == CharacterType.Minion))
        {
            warnings.Add("Script has no Minion characters.");
        }
    }

    private static string ReadStringOrDefault(JsonElement element, string propertyName, string defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return defaultValue;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }
}

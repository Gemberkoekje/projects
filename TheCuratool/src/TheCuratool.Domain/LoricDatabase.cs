using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace TheCuratool.Domain;

/// <summary>
/// In-memory registry of <see cref="LoricDefinition"/> records loaded from <c>lorics.json</c>.
/// </summary>
public sealed class LoricDatabase
{
    private readonly IReadOnlyDictionary<string, LoricDefinition> _lorics;

    /// <summary>Initialises the database from a pre-built dictionary of Loric definitions.</summary>
    /// <param name="lorics">The Loric definitions keyed by their canonical ID (case-insensitive).</param>
    public LoricDatabase(IReadOnlyDictionary<string, LoricDefinition> lorics)
    {
        _lorics = new ReadOnlyDictionary<string, LoricDefinition>(new Dictionary<string, LoricDefinition>(lorics, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Loads and parses a <see cref="LoricDatabase"/> from a JSON file on disk.</summary>
    /// <param name="filePath">Absolute or relative path to <c>lorics.json</c>.</param>
    public static LoricDatabase LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return new LoricDatabase(Parse(json));
    }

    /// <summary>Returns all known Loric definitions.</summary>
    public IReadOnlyCollection<LoricDefinition> GetAll()
    {
        return _lorics.Values.ToList().AsReadOnly();
    }

    private static IReadOnlyDictionary<string, LoricDefinition> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("lorics", out var loricsElement) || loricsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Expected root property 'lorics' with array value.");
        }

        var lorics = new Dictionary<string, LoricDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in loricsElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("Loric id is required.");
            }

            var normalizedId = id.Trim();
            var displayName = ReadStringOrDefault(item, "displayName", normalizedId);
            var setupRules = ParseSetupRules(item, normalizedId);
            lorics[normalizedId] = new LoricDefinition(normalizedId, displayName, setupRules);
        }

        return new ReadOnlyDictionary<string, LoricDefinition>(lorics);
    }

    private static IReadOnlyList<ISetupRule> ParseSetupRules(JsonElement item, string loricId)
    {
        if (!item.TryGetProperty("setupRules", out var setupElement) || setupElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<ISetupRule>();
        }

        var rules = new List<ISetupRule>();

        foreach (var rule in setupElement.EnumerateArray())
        {
            rules.Add(new LoricSetupRule(loricId, ParseSetupRule(rule)));
        }

        return rules.AsReadOnly();
    }

    private static ISetupRule ParseSetupRule(JsonElement rule)
    {
        var kind = ReadStringOrDefault(rule, "kind", string.Empty);

        if (string.Equals(kind, "OutsiderDelta", StringComparison.OrdinalIgnoreCase))
        {
            var delta = rule.GetProperty("delta").GetInt32();
            return new OutsiderDeltaSetupRule(delta, false);
        }

        if (string.Equals(kind, "StoryTellerChoice", StringComparison.OrdinalIgnoreCase)
            && rule.TryGetProperty("options", out var options)
            && options.ValueKind == JsonValueKind.Array)
        {
            var choices = new List<ISetupRule>();
            foreach (var option in options.EnumerateArray())
            {
                choices.Add(ParseSetupRule(option));
            }

            if (choices.Count == 0)
            {
                return new NoSetupRule();
            }

            return new StoryTellerChoiceSetupRule(choices.AsReadOnly());
        }

        return new NoSetupRule();
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

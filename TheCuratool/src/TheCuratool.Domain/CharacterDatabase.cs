using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace TheCuratool.Domain;

/// <summary>
/// In-memory registry of <see cref="CharacterDefinition"/> records loaded from <c>characters.json</c>.
/// Provides character resolution with graceful fallback for unknown IDs.
/// </summary>
public sealed class CharacterDatabase
{
    private readonly IReadOnlyDictionary<string, CharacterDefinition> _characters;

    /// <summary>Initialises the database from a pre-built dictionary of character definitions.</summary>
    /// <param name="characters">The character definitions keyed by their canonical ID (case-insensitive).</param>
    public CharacterDatabase(IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        _characters = new ReadOnlyDictionary<string, CharacterDefinition>(new Dictionary<string, CharacterDefinition>(characters, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Loads and parses a <see cref="CharacterDatabase"/> from a JSON file on disk.</summary>
    /// <param name="filePath">Absolute or relative path to <c>characters.json</c>.</param>
    public static CharacterDatabase LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return new CharacterDatabase(Parse(json));
    }

    /// <summary>Returns all known character definitions.</summary>
    public IReadOnlyCollection<CharacterDefinition> GetAll()
    {
        return _characters.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Returns the <see cref="CharacterDefinition"/> for the given <paramref name="id"/>.
    /// If the ID is not found in the database, a synthetic definition is returned with
    /// <see cref="CharacterDefinition.IsUnknown"/> set to <see langword="true"/>.
    /// </summary>
    /// <param name="id">The canonical character ID to look up (case-insensitive).</param>
    public CharacterDefinition Resolve(string id)
    {
        if (_characters.TryGetValue(id, out var definition))
        {
            return definition;
        }

        return new CharacterDefinition(
            id,
            ToTitleCase(id),
            CharacterType.Townsfolk,
            Array.Empty<ISetupRule>(),
            Array.Empty<IAvailabilityConstraint>(),
            true,
            false,
            false);
    }

    private static CharacterDefinition CreateDefinition(
        string id,
        string displayName,
        CharacterType type,
        IReadOnlyList<ISetupRule> setupRules,
        IReadOnlyList<IAvailabilityConstraint> availabilityConstraints,
        bool isUnknown,
        bool isDraftExcluded,
        bool isOutOfScript,
        bool isDynamicSetup = false,
        DynamicAbilityScope dynamicAbilityScope = DynamicAbilityScope.Unknown)
    {
        return new CharacterDefinition(
            id,
            displayName,
            type,
            setupRules,
            availabilityConstraints,
            isUnknown,
            isDraftExcluded,
            isOutOfScript,
            isDynamicSetup,
            dynamicAbilityScope);
    }

    private static IReadOnlyDictionary<string, CharacterDefinition> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("characters", out var charactersElement) || charactersElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Expected root property 'characters' with array value.");
        }

        var characters = new Dictionary<string, CharacterDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in charactersElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidDataException("Character id is required.");
            }

            var normalizedId = id.Trim();
            var displayName = ReadStringOrDefault(item, "displayName", ToTitleCase(normalizedId));
            var type = ParseCharacterType(ReadStringOrDefault(item, "type", CharacterType.Townsfolk.ToString()));
            var setupRules = ParseSetupRules(item);
            var constraints = ParseAvailabilityConstraints(item);
            var isDraftExcluded = ReadBoolOrDefault(item, "isDraftExcluded", false);
            var isDynamicSetup = ReadBoolOrDefault(item, "dynamicSetup", false);
            var dynamicAbilityScope = ParseDynamicAbilityScope(ReadStringOrDefault(item, "dynamicAbilityScope", DynamicAbilityScope.Unknown.ToString()));

            characters[normalizedId] = CreateDefinition(
                normalizedId,
                displayName,
                type,
                setupRules,
                constraints,
                false,
                isDraftExcluded,
                false,
                isDynamicSetup,
                dynamicAbilityScope);
        }

        return new ReadOnlyDictionary<string, CharacterDefinition>(characters);
    }

    private static IReadOnlyList<ISetupRule> ParseSetupRules(JsonElement item)
    {
        if (!TryGetArray(item, "setupRules", out var setupElement) && !TryGetArray(item, "setup", out setupElement))
        {
            return Array.Empty<ISetupRule>();
        }

        var rules = new List<ISetupRule>();

        foreach (var rule in setupElement.EnumerateArray())
        {
            rules.Add(ParseSetupRule(rule));
        }

        return rules.AsReadOnly();
    }

    private static ISetupRule ParseSetupRule(JsonElement rule)
    {
        var kind = ReadStringOrDefault(rule, "kind", string.Empty);

        if (string.Equals(kind, "OutsiderDelta", StringComparison.OrdinalIgnoreCase))
        {
            var delta = rule.GetProperty("delta").GetInt32();
            var isStorytellerChoice = ReadBoolOrDefault(rule, "isStorytellerChoice", false);
            return new OutsiderDeltaSetupRule(delta, isStorytellerChoice);
        }

        if (string.Equals(kind, "ReplaceDemon", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplaceDemonSetupRule();
        }

        if (string.Equals(kind, "SwapOutsiderForTownsfolk", StringComparison.OrdinalIgnoreCase))
        {
            return new SwapOutsiderForTownsfolkSetupRule();
        }

        if (string.Equals(kind, "MinionDelta", StringComparison.OrdinalIgnoreCase))
        {
            var delta = rule.GetProperty("delta").GetInt32();
            return new MinionDeltaSetupRule(delta);
        }

        if (string.Equals(kind, "UnconstrainedOutsiderDelta", StringComparison.OrdinalIgnoreCase))
        {
            return new UnconstrainedOutsiderDeltaSetupRule();
        }

        if (string.Equals(kind, "RemoveAllMinions", StringComparison.OrdinalIgnoreCase))
        {
            return new RemoveAllMinionsSetupRule();
        }

        if (string.Equals(kind, "ReplaceTownsfolk", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplaceTownsfolkSetupRule();
        }

        if (string.Equals(kind, "RequiresCharacter", StringComparison.OrdinalIgnoreCase))
        {
            var requiredId = ReadStringOrDefault(rule, "requiredId", string.Empty);
            var autoAddIfMissing = ReadBoolOrDefault(rule, "autoAddIfMissing", false);
            return new RequiresCharacterSetupRule(requiredId, autoAddIfMissing);
        }

        if (string.Equals(kind, "MinionSwap", StringComparison.OrdinalIgnoreCase))
        {
            return new MinionSwapSetupRule();
        }

        if (string.Equals(kind, "ReplaceDemonOutsiderDisplay", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplaceDemonOutsiderDisplayRule();
        }

        if (string.Equals(kind, "StoryTellerChoice", StringComparison.OrdinalIgnoreCase)
            && TryGetArray(rule, "options", out var optionsElement))
        {
            var options = new List<ISetupRule>();
            foreach (var option in optionsElement.EnumerateArray())
            {
                options.Add(ParseSetupRule(option));
            }

            if (options.Count == 0)
            {
                return new NoSetupRule();
            }

            return new StoryTellerChoiceSetupRule(options.AsReadOnly());
        }

        return new NoSetupRule();
    }

    private static IReadOnlyList<IAvailabilityConstraint> ParseAvailabilityConstraints(JsonElement item)
    {
        if (!TryGetArray(item, "availabilityConstraints", out var constraintsElement))
        {
            return Array.Empty<IAvailabilityConstraint>();
        }

        var constraints = new List<IAvailabilityConstraint>();

        foreach (var constraint in constraintsElement.EnumerateArray())
        {
            var kind = ReadStringOrDefault(constraint, "kind", string.Empty);

            if (string.Equals(kind, "BlockedIfAnyChosenOfType", StringComparison.OrdinalIgnoreCase))
            {
                var typeText = ReadStringOrDefault(constraint, "type", CharacterType.Unknown.ToString());
                var type = ParseCharacterType(typeText);
                constraints.Add(new BlockedIfAnyChosenOfTypeConstraint(type));
                continue;
            }

        }

        return constraints.AsReadOnly();
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement array)
    {
        if (element.TryGetProperty(propertyName, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
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

    private static bool ReadBoolOrDefault(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var property) || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return defaultValue;
        }

        return property.GetBoolean();
    }

    private static CharacterType ParseCharacterType(string value)
    {
        if (Enum.TryParse<CharacterType>(value, true, out var parsed))
        {
            return parsed;
        }

        return CharacterType.Unknown;
    }

    private static DynamicAbilityScope ParseDynamicAbilityScope(string value)
    {
        if (Enum.TryParse<DynamicAbilityScope>(value, true, out var parsed))
        {
            return parsed;
        }

        return DynamicAbilityScope.Unknown;
    }

    private static string ToTitleCase(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return string.Empty;
        }

        if (id.Length == 1)
        {
            return id.ToUpperInvariant();
        }

        var first = char.ToUpper(id[0], CultureInfo.InvariantCulture);
        return first + id[1..];
    }
}

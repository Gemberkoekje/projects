using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Sts2Extractor.Cli;
using Sts2Extractor.Configuration;

namespace Sts2Extractor.Localization;

internal sealed class LocStringReader
{
    private static readonly Regex SmartFormatRegex = new Regex("\\{[^{}]+\\}", RegexOptions.Compiled);
    private static readonly Regex BbCodeRegex = new Regex("\\[(?:/?)[^\\]]+\\]", RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _cards;
    private readonly Dictionary<string, string> _relics;
    private readonly Dictionary<string, string> _potions;
    private readonly Dictionary<string, string> _powers;
    private readonly Dictionary<string, string> _events;
    private readonly Dictionary<string, string> _characters;
    private readonly HashSet<string> _missingWarnings;

    private LocStringReader(string englishDirectory)
    {
        _cards = LoadTable(Path.Combine(englishDirectory, "cards.json"));
        _relics = LoadTable(Path.Combine(englishDirectory, "relics.json"));
        _potions = LoadTable(Path.Combine(englishDirectory, "potions.json"));
        _powers = LoadTable(Path.Combine(englishDirectory, "powers.json"));
        _events = LoadTable(Path.Combine(englishDirectory, "events.json"));
        _characters = LoadTable(Path.Combine(englishDirectory, "characters.json"));
        _missingWarnings = new HashSet<string>(StringComparer.Ordinal);
    }

    public static LocStringReader Create(CliOptions options)
    {
        string configuredPath = ResolveConfiguredEnglishDirectory();

        List<string> candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            candidates.Add(configuredPath);
            if (!Path.IsPathRooted(configuredPath))
            {
                candidates.Add(Path.Combine(options.RootPath, configuredPath));
            }
        }

        candidates.Add(Path.Combine(options.RootPath, "sts2", "eng"));
        candidates.Add(Path.Combine(options.RootPath, "eng"));
        candidates.Add(Path.Combine(options.RootPath, "localization", "eng"));

        string englishDirectory = candidates
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists) ?? string.Empty;

        if (englishDirectory.Length == 0)
        {
            Console.Error.WriteLine("[loc] Could not find localization directory. Checked appconfig/env and common paths under --root.");
        }

        return new LocStringReader(englishDirectory);
    }

    public string GetCardTitle(string cardId) => Lookup(_cards, BuildEntityKey(cardId, "title"), "cards");

    public string GetCardDescription(string cardId) => Lookup(_cards, BuildEntityKey(cardId, "description"), "cards");

    public string GetRelicTitle(string relicId) => Lookup(_relics, BuildEntityKey(relicId, "title"), "relics");

    public string GetRelicDescription(string relicId) => Lookup(_relics, BuildEntityKey(relicId, "description"), "relics");

    public string GetRelicFlavor(string relicId) => Lookup(_relics, BuildEntityKey(relicId, "flavor"), "relics");

    public string GetPotionTitle(string potionId) => Lookup(_potions, BuildEntityKey(potionId, "title"), "potions");

    public string GetPotionDescription(string potionId) => Lookup(_potions, BuildEntityKey(potionId, "description"), "potions");

    public string GetPowerTitle(string powerId)
    {
        List<(IReadOnlyDictionary<string, string> Table, string Key)> candidates = BuildPowerCandidates(powerId, "title");
        string value = LookupAny(candidates, "powers");
        if (value.Length > 0)
        {
            return value;
        }

        return string.Empty;
    }

    public string GetPowerDescription(string powerId)
    {
        List<(IReadOnlyDictionary<string, string> Table, string Key)> candidates = BuildPowerCandidates(powerId, "description");
        string value = LookupAny(candidates, "powers");
        if (value.Length > 0)
        {
            return value;
        }

        return string.Empty;
    }

    public string GetEventTitle(string eventId) => Lookup(_events, BuildEntityKey(eventId, "title"), "events");

    public string GetEventDescription(string eventId)
    {
        string initial = GetEventPageDescription(eventId, "INITIAL");
        if (initial.Length > 0)
        {
            return initial;
        }

        string normalizedEvent = NormalizeEntityId(eventId);
        string prefix = normalizedEvent + ".pages.";

        string page = _events.Keys
            .FirstOrDefault(key => key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(".description", StringComparison.Ordinal) && !key.Contains(".options.", StringComparison.Ordinal)) ?? string.Empty;

        return page.Length == 0 ? string.Empty : Lookup(_events, page, "events");
    }

    public string GetEventPageDescription(string eventId, string pageKey)
    {
        string normalizedEvent = NormalizeEntityId(eventId);
        string normalizedPage = NormalizeEntityId(pageKey);
        return Lookup(_events, $"{normalizedEvent}.pages.{normalizedPage}.description", "events");
    }

    public string GetEventOptionTitle(string eventId, string pageKey, string optionKey)
    {
        return GetEventOptionValue(eventId, pageKey, optionKey, "title");
    }

    public string GetEventOptionDescription(string eventId, string pageKey, string optionKey)
    {
        return GetEventOptionValue(eventId, pageKey, optionKey, "description");
    }

    public string GetCharacterTitle(string characterId)
    {
        return GetCharacterValue(characterId, "title");
    }

    public string GetCharacterDescription(string characterId)
    {
        return GetCharacterValue(characterId, "description");
    }

    public static string StripFormatting(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string stripped = SmartFormatRegex.Replace(raw, string.Empty);
        stripped = BbCodeRegex.Replace(stripped, string.Empty);
        stripped = MultiWhitespaceRegex.Replace(stripped, " ").Trim();
        return stripped;
    }

    private string GetEventOptionValue(string eventId, string pageKey, string optionKey, string field)
    {
        string normalizedEvent = NormalizeEntityId(eventId);
        string normalizedPage = NormalizeEntityId(pageKey);
        string normalizedOption = NormalizeEntityId(optionKey);

        string directKey = $"{normalizedEvent}.pages.{normalizedPage}.options.{normalizedOption}.{field}";
        string direct = Lookup(_events, directKey, "events", logMissing: false);
        if (direct.Length > 0)
        {
            return direct;
        }

        List<string> optionKeyCandidates = BuildEventOptionCandidates(normalizedEvent, normalizedPage, normalizedOption, field);
        for (int i = 0; i < optionKeyCandidates.Count; i++)
        {
            string candidate = optionKeyCandidates[i];
            string candidateValue = Lookup(_events, candidate, "events", logMissing: false);
            if (candidateValue.Length > 0)
            {
                return candidateValue;
            }
        }

        string partial = $".options.{normalizedOption}.{field}";
        string fallback = _events
            .Where(kvp => kvp.Key.StartsWith(normalizedEvent + ".pages.", StringComparison.Ordinal) && kvp.Key.EndsWith(partial, StringComparison.Ordinal))
            .Select(kvp => StripFormatting(kvp.Value))
            .FirstOrDefault(static x => x.Length > 0) ?? string.Empty;

        if (fallback.Length > 0)
        {
            return fallback;
        }

        WarnMissing("events", directKey);
        return string.Empty;
    }

    private string GetCharacterValue(string characterId, string field)
    {
        List<string> keys = BuildCharacterKeys(characterId)
            .Select(id => id + "." + field)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        for (int i = 0; i < keys.Count; i++)
        {
            string value = Lookup(_characters, keys[i], "characters", logMissing: false);
            if (value.Length > 0)
            {
                return value;
            }
        }

        WarnMissing("characters", keys.FirstOrDefault() ?? string.Empty);
        return string.Empty;
    }

    private static List<string> BuildCharacterKeys(string characterId)
    {
        string normalized = NormalizeEntityId(characterId);
        List<string> keys = new List<string> { normalized };

        if (string.Equals(normalized, "REGENT", StringComparison.Ordinal))
        {
            keys.Add("WATCHER");
        }
        else if (string.Equals(normalized, "WATCHER", StringComparison.Ordinal))
        {
            keys.Add("REGENT");
        }

        if (string.Equals(normalized, "NECROBINDER", StringComparison.Ordinal))
        {
            keys.Add("DEPRIVED");
        }
        else if (string.Equals(normalized, "DEPRIVED", StringComparison.Ordinal))
        {
            keys.Add("NECROBINDER");
        }

        return keys;
    }

    private List<(IReadOnlyDictionary<string, string> Table, string Key)> BuildPowerCandidates(string powerId, string field)
    {
        string normalized = NormalizeEntityId(powerId);
        string powerKey = normalized.EndsWith("_POWER", StringComparison.Ordinal) ? normalized : normalized + "_POWER";
        string baseKey = powerKey.EndsWith("_POWER", StringComparison.Ordinal)
            ? powerKey.Substring(0, powerKey.Length - "_POWER".Length)
            : powerKey;

        List<(IReadOnlyDictionary<string, string> Table, string Key)> candidates = new List<(IReadOnlyDictionary<string, string> Table, string Key)>
        {
            (_powers, powerKey + "." + field),
            (_powers, baseKey + "." + field)
        };

        if (string.Equals(field, "description", StringComparison.Ordinal))
        {
            candidates.Add((_powers, powerKey + ".smartDescription"));
            candidates.Add((_powers, baseKey + ".smartDescription"));
        }

        candidates.Add((_cards, baseKey + "." + field));
        candidates.Add((_relics, baseKey + "." + field));
        candidates.Add((_potions, baseKey + "." + field));

        return candidates;
    }

    private string LookupAny(IEnumerable<(IReadOnlyDictionary<string, string> Table, string Key)> candidates, string tableName)
    {
        foreach ((IReadOnlyDictionary<string, string> Table, string Key) candidate in candidates)
        {
            string value = Lookup(candidate.Table, candidate.Key, tableName, logMissing: false);
            if (value.Length > 0)
            {
                return value;
            }
        }

        (IReadOnlyDictionary<string, string> Table, string Key) first = candidates.FirstOrDefault();
        if (first.Key.Length > 0)
        {
            WarnMissing(tableName, first.Key);
        }

        return string.Empty;
    }

    private static List<string> BuildEventOptionCandidates(string eventKey, String pageKey, string optionKey, string field)
    {
        List<string> optionVariants = new List<string> { optionKey };

        string pagePrefix = eventKey + "_PAGES_" + pageKey + "_OPTIONS_";
        if (optionKey.StartsWith(pagePrefix, StringComparison.Ordinal) && optionKey.Length > pagePrefix.Length)
        {
            optionVariants.Add(optionKey.Substring(pagePrefix.Length));
        }

        string initialPrefix = eventKey + "_PAGES_INITIAL_OPTIONS_";
        if (optionKey.StartsWith(initialPrefix, StringComparison.Ordinal) && optionKey.Length > initialPrefix.Length)
        {
            optionVariants.Add(optionKey.Substring(initialPrefix.Length));
        }

        string eventPrefix = eventKey + "_OPTIONS_";
        if (optionKey.StartsWith(eventPrefix, StringComparison.Ordinal) && optionKey.Length > eventPrefix.Length)
        {
            optionVariants.Add(optionKey.Substring(eventPrefix.Length));
        }

        return optionVariants
            .Distinct(StringComparer.Ordinal)
            .Select(v => $"{eventKey}.pages.{pageKey}.options.{v}.{field}")
            .ToList();
    }

    private string Lookup(IReadOnlyDictionary<string, string> table, string key, string tableName, bool logMissing = true)
    {
        if (table.TryGetValue(key, out string value))
        {
            return StripFormatting(value);
        }

        if (logMissing)
        {
            WarnMissing(tableName, key);
        }

        return string.Empty;
    }

    private void WarnMissing(string tableName, string key)
    {
        string marker = tableName + "::" + key;
        if (_missingWarnings.Add(marker))
        {
            Console.Error.WriteLine($"[loc] Missing key '{key}' in {tableName} localization table.");
        }
    }

    private static Dictionary<string, string> LoadTable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            string json = File.ReadAllText(path);
            Dictionary<string, string> data = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            return new Dictionary<string, string>(data, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[loc] Failed to load '{path}': {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static string BuildEntityKey(string entityId, string field)
    {
        return NormalizeEntityId(entityId) + "." + field;
    }

    private static string BuildPowerKey(string powerId, string field)
    {
        string normalized = NormalizeEntityId(powerId);
        if (!normalized.EndsWith("_POWER", StringComparison.Ordinal))
        {
            normalized += "_POWER";
        }

        return normalized + "." + field;
    }

    private static string NormalizeEntityId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length * 2);
        char previous = '\0';

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                bool isUpper = char.IsUpper(c);
                bool previousIsLowerOrDigit = previous != '\0' && (char.IsLower(previous) || char.IsDigit(previous));
                bool previousIsUpper = previous != '\0' && char.IsUpper(previous);
                bool nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (builder.Length > 0 && isUpper && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)) && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToUpperInvariant(c));
            }
            else
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }
            }

            previous = c;
        }

        return builder.ToString().Trim('_');
    }

    private static string ResolveConfiguredEnglishDirectory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appconfig.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<UserSecretsMarker>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        string configured = configuration["Localization:EnglishDirectory"] ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Environment.GetEnvironmentVariable("STS2_LOCALIZATION_ENG_DIRECTORY") ?? string.Empty;
    }
}

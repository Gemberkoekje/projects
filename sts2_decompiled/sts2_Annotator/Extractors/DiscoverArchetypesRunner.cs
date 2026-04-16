using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Npgsql;
using NpgsqlTypes;
using Sts2Extractor.Annotation;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;

namespace Sts2Extractor.Extractors;

internal sealed class DiscoverArchetypesRunner
{
    public DiscoverArchetypesResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.ArchetypeDiscovery)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options, model);
        List<string> characters = LoadCharacterIds(connectionString, options);
        List<ArchetypeOutputRow> outputRows = new List<ArchetypeOutputRow>();

        for (int i = 0; i < characters.Count; i++)
        {
            string characterId = characters[i];
            Console.WriteLine($"[discover-archetypes] ({i + 1}/{characters.Count}) {characterId}");

            List<CardPromptRow> cards = LoadCards(connectionString, characterId);
            if (cards.Count == 0)
            {
                cards = LoadCardsByPool(connectionString, options.RootPath, characterId);
            }

            List<RelicPromptRow> relics = LoadRelics(connectionString, characterId);
            if (relics.Count == 0)
            {
                relics = LoadRelicsByPool(connectionString, options.RootPath, characterId);
            }

            if (cards.Count == 0)
            {
                continue;
            }

            string prompt = BuildPrompt(characterId, cards, relics);
            AnnotationRequest request = new AnnotationRequest
            {
                Model = model,
                SystemPrompt = "You are an expert Slay the Spire 2 deckbuilding analyst. Return only valid JSON.",
                Prompt = prompt,
                MaxTokens = Math.Max(options.MaxTokens, 1800)
            };

            AnnotationResult annotation = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            List<ArchetypeDefinition> discovered = ParseArchetypes(annotation.Content);
            if (discovered.Count == 0)
            {
                Console.WriteLine("  No archetypes parsed from model response.");
                continue;
            }

            UpsertCharacterArchetypes(connectionString, characterId, discovered);

            for (int d = 0; d < discovered.Count; d++)
            {
                ArchetypeDefinition archetype = discovered[d];
                outputRows.Add(new ArchetypeOutputRow
                {
                    CharacterId = characterId,
                    Name = archetype.Name,
                    Description = archetype.Description,
                    KeyEffectTags = string.Join(';', archetype.KeyEffectTags),
                    CoreCardIds = string.Join(';', archetype.CoreCardIds)
                });
            }
        }

        CsvWriter.Write(options.OutputPath, new[]
        {
            "character_id",
            "name",
            "description",
            "key_effect_tags",
            "core_card_ids"
        }, outputRows.Select(static r => (IReadOnlyList<string>)new[]
        {
            r.CharacterId,
            r.Name,
            r.Description,
            r.KeyEffectTags,
            r.CoreCardIds
        }));

        return new DiscoverArchetypesResult
        {
            OutputPath = options.OutputPath,
            CharacterCount = characters.Count,
            ArchetypeCount = outputRows.Count
        };
    }

    private static List<string> LoadCharacterIds(string connectionString, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CharacterId))
        {
            return new List<string> { options.CharacterId.Trim().ToLowerInvariant() };
        }

        List<string> ids = new List<string>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT DISTINCT LOWER(id)
FROM characters
WHERE id <> ''
ORDER BY LOWER(id);", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static List<CardPromptRow> LoadCards(string connectionString, string characterId)
    {
        List<CardPromptRow> rows = new List<CardPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(title, id),
    COALESCE(type, ''),
    COALESCE(rarity, ''),
    COALESCE(energy_cost, -1),
    COALESCE(keywords, ARRAY[]::text[]),
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(effect_description, '')
FROM cards
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("character_id", characterId);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CardPromptRow
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Type = reader.GetString(2),
                Rarity = reader.GetString(3),
                EnergyCost = reader.GetInt32(4),
                Keywords = reader.GetFieldValue<string[]>(5),
                EffectTags = reader.GetFieldValue<string[]>(6),
                EffectDescription = reader.GetString(7)
            });
        }

        return rows;
    }

    private static List<RelicPromptRow> LoadRelics(string connectionString, string characterId)
    {
        List<RelicPromptRow> rows = new List<RelicPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(title, id),
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(effect_description, '')
FROM relics
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("character_id", characterId);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RelicPromptRow
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                EffectTags = reader.GetFieldValue<string[]>(2),
                EffectDescription = reader.GetString(3)
            });
        }

        return rows;
    }

    private static List<CardPromptRow> LoadCardsByPool(string connectionString, string rootPath, string characterId)
    {
        HashSet<string> classNames;
        try
        {
            classNames = CharacterPoolReader.ReadCardClassNames(rootPath, characterId);
        }
        catch (FileNotFoundException)
        {
            return new List<CardPromptRow>();
        }

        if (classNames.Count == 0)
        {
            return new List<CardPromptRow>();
        }

        List<CardPromptRow> rows = new List<CardPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(title, id),
    COALESCE(type, ''),
    COALESCE(rarity, ''),
    COALESCE(energy_cost, -1),
    COALESCE(keywords, ARRAY[]::text[]),
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(effect_description, '')
FROM cards
WHERE id = ANY(@ids)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("ids", classNames.ToArray());

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CardPromptRow
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Type = reader.GetString(2),
                Rarity = reader.GetString(3),
                EnergyCost = reader.GetInt32(4),
                Keywords = reader.GetFieldValue<string[]>(5),
                EffectTags = reader.GetFieldValue<string[]>(6),
                EffectDescription = reader.GetString(7)
            });
        }

        return rows;
    }

    private static List<RelicPromptRow> LoadRelicsByPool(string connectionString, string rootPath, string characterId)
    {
        HashSet<string> classNames;
        try
        {
            classNames = CharacterPoolReader.ReadRelicClassNames(rootPath, characterId);
        }
        catch (FileNotFoundException)
        {
            return new List<RelicPromptRow>();
        }

        if (classNames.Count == 0)
        {
            return new List<RelicPromptRow>();
        }

        List<RelicPromptRow> rows = new List<RelicPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT
    id,
    COALESCE(title, id),
    COALESCE(effect_tags, ARRAY[]::text[]),
    COALESCE(effect_description, '')
FROM relics
WHERE id = ANY(@ids)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("ids", classNames.ToArray());

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RelicPromptRow
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                EffectTags = reader.GetFieldValue<string[]>(2),
                EffectDescription = reader.GetString(3)
            });
        }

        return rows;
    }

    private static string BuildPrompt(string characterId, IReadOnlyList<CardPromptRow> cards, IReadOnlyList<RelicPromptRow> relics)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("You are an expert Slay the Spire 2 deckbuilding analyst.");
        builder.AppendLine();
        builder.AppendLine("Given the following cards and relics for the specified character, identify 4-8 distinct archetypes.");
        builder.AppendLine("Each archetype should represent a recognizable gameplay concept or a specific build-around card.");
        builder.AppendLine();
        builder.AppendLine("Return a JSON array only. Each element must contain:");
        builder.AppendLine("- name (string, 1-3 words)");
        builder.AppendLine("- description (string)");
        builder.AppendLine("- key_effect_tags (array of 2-5 strings)");
        builder.AppendLine("- core_card_ids (array of 3-6 card IDs)");
        builder.AppendLine();
        builder.AppendLine("Character:");
        builder.AppendLine(characterId);
        builder.AppendLine();
        builder.AppendLine("Cards:");
        builder.AppendLine(JsonSerializer.Serialize(cards));
        builder.AppendLine();
        builder.AppendLine("Relics:");
        builder.AppendLine(JsonSerializer.Serialize(relics));

        return builder.ToString();
    }

    private static List<ArchetypeDefinition> ParseArchetypes(string content)
    {
        List<ArchetypeDefinition> rows = new List<ArchetypeDefinition>();
        string cleaned = CleanJson(content);

        using JsonDocument document = JsonDocument.Parse(cleaned);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            string name = ReadString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string description = ReadString(element, "description");
            string[] keyTags = ReadStringArray(element, "key_effect_tags");
            string[] coreCards = ReadStringArray(element, "core_card_ids");
            if (keyTags.Length == 0)
            {
                continue;
            }

            rows.Add(new ArchetypeDefinition
            {
                Name = name.Trim(),
                Description = description.Trim(),
                KeyEffectTags = keyTags,
                CoreCardIds = coreCards
            });
        }

        return rows;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return string.Empty;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string[] ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        List<string> values = new List<string>();
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string value = (item.GetString() ?? string.Empty).Trim();
                if (value.Length > 0)
                {
                    values.Add(value);
                }
            }
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string CleanJson(string content)
    {
        string cleaned = (content ?? string.Empty).Trim();
        if (!cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            return cleaned;
        }

        int firstNewline = cleaned.IndexOf('\n');
        if (firstNewline >= 0)
        {
            cleaned = cleaned.Substring(firstNewline + 1);
        }

        int lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
        {
            cleaned = cleaned.Substring(0, lastFence);
        }

        return cleaned.Trim();
    }

    private static void UpsertCharacterArchetypes(string connectionString, string characterId, List<ArchetypeDefinition> archetypes)
    {
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction();

        using (NpgsqlCommand deleteAffinity = new NpgsqlCommand(@"
DELETE FROM entity_archetype_affinity
WHERE archetype_id IN (
    SELECT id FROM archetypes WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
);", connection, transaction))
        {
            deleteAffinity.Parameters.AddWithValue("character_id", characterId);
            deleteAffinity.ExecuteNonQuery();
        }

        using (NpgsqlCommand deleteArchetypes = new NpgsqlCommand("DELETE FROM archetypes WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id);", connection, transaction))
        {
            deleteArchetypes.Parameters.AddWithValue("character_id", characterId);
            deleteArchetypes.ExecuteNonQuery();
        }

        for (int i = 0; i < archetypes.Count; i++)
        {
            ArchetypeDefinition archetype = archetypes[i];
            using NpgsqlCommand insert = new NpgsqlCommand(@"
INSERT INTO archetypes (character_id, name, description, key_effect_tags)
VALUES (@character_id, @name, @description, @key_effect_tags);", connection, transaction);

            insert.Parameters.AddWithValue("character_id", characterId);
            insert.Parameters.AddWithValue("name", archetype.Name);
            insert.Parameters.AddWithValue("description", archetype.Description);
            insert.Parameters.Add("key_effect_tags", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = archetype.KeyEffectTags;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private sealed class CardPromptRow
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Type { get; set; }

        public string Rarity { get; set; }

        public int EnergyCost { get; set; }

        public string[] Keywords { get; set; }

        public string[] EffectTags { get; set; }

        public string EffectDescription { get; set; }

        public CardPromptRow()
        {
            Id = string.Empty;
            Title = string.Empty;
            Type = string.Empty;
            Rarity = string.Empty;
            Keywords = Array.Empty<string>();
            EffectTags = Array.Empty<string>();
            EffectDescription = string.Empty;
        }
    }

    private sealed class RelicPromptRow
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string[] EffectTags { get; set; }

        public string EffectDescription { get; set; }

        public RelicPromptRow()
        {
            Id = string.Empty;
            Title = string.Empty;
            EffectTags = Array.Empty<string>();
            EffectDescription = string.Empty;
        }
    }

    private sealed class ArchetypeDefinition
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string[] KeyEffectTags { get; set; }

        public string[] CoreCardIds { get; set; }

        public ArchetypeDefinition()
        {
            Name = string.Empty;
            Description = string.Empty;
            KeyEffectTags = Array.Empty<string>();
            CoreCardIds = Array.Empty<string>();
        }
    }

    private sealed class ArchetypeOutputRow
    {
        public string CharacterId { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string KeyEffectTags { get; set; }

        public string CoreCardIds { get; set; }

        public ArchetypeOutputRow()
        {
            CharacterId = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            KeyEffectTags = string.Empty;
            CoreCardIds = string.Empty;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Npgsql;
using Sts2Extractor.Annotation;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;

namespace Sts2Extractor.Extractors;

internal sealed class ScoreAffinitiesRunner
{
    private const int MaxAffinityBatchSize = 20;

    public ScoreAffinitiesResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.AffinityScoring)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options.Provider);
        List<string> characters = LoadTargetCharacters(connectionString, options);
        List<EntityArchetypeAffinityRecord> allRows = new List<EntityArchetypeAffinityRecord>();

        int totalEntities = 0;
        int batchCount = 0;

        for (int i = 0; i < characters.Count; i++)
        {
            string characterId = characters[i];
            List<ArchetypeRow> archetypes = LoadArchetypes(connectionString, characterId);
            if (archetypes.Count == 0)
            {
                continue;
            }

            List<EntityPromptRow> entities = LoadEntities(connectionString, characterId);
            if (entities.Count == 0)
            {
                entities = LoadEntitiesByPool(connectionString, options.RootPath, characterId);
            }
            if (entities.Count == 0)
            {
                continue;
            }

            totalEntities += entities.Count;
            Console.WriteLine($"[score-affinities] ({i + 1}/{characters.Count}) {characterId}: {entities.Count} entities");

            int batchSize = Math.Max(1, Math.Min(options.BatchSize, MaxAffinityBatchSize));
            for (int start = 0; start < entities.Count; start += batchSize)
            {
                List<EntityPromptRow> batch = entities.Skip(start).Take(batchSize).ToList();
                Dictionary<string, Dictionary<string, int>> scored = ScoreBatch(provider, model, characterId, archetypes, batch, options.MaxTokens);

                for (int b = 0; b < batch.Count; b++)
                {
                    EntityPromptRow entity = batch[b];
                    if (!scored.TryGetValue(entity.Id, out Dictionary<string, int> entityAffinities))
                    {
                        continue;
                    }

                    for (int a = 0; a < archetypes.Count; a++)
                    {
                        ArchetypeRow archetype = archetypes[a];
                        int affinity = entityAffinities.TryGetValue(archetype.Name, out int value) ? value : 0;
                        affinity = Math.Max(0, Math.Min(10, affinity));

                        allRows.Add(new EntityArchetypeAffinityRecord
                        {
                            ArchetypeId = archetype.Id,
                            CharacterId = characterId,
                            ArchetypeName = archetype.Name,
                            EntityType = entity.EntityType,
                            EntityId = entity.Id,
                            AffinityScore = affinity
                        });
                    }
                }

                batchCount++;
            }
        }

        UpsertRows(connectionString, allRows);

        CsvWriter.Write(options.OutputPath, new[]
        {
            "archetype_id",
            "character_id",
            "archetype_name",
            "entity_type",
            "entity_id",
            "affinity_score"
        }, allRows.Select(static row => (IReadOnlyList<string>)new[]
        {
            row.ArchetypeId.ToString(CultureInfo.InvariantCulture),
            row.CharacterId,
            row.ArchetypeName,
            row.EntityType,
            row.EntityId,
            row.AffinityScore.ToString(CultureInfo.InvariantCulture)
        }));

        return new ScoreAffinitiesResult
        {
            OutputPath = options.OutputPath,
            CharacterCount = characters.Count,
            EntityCount = totalEntities,
            AffinityCount = allRows.Count,
            BatchCount = batchCount
        };
    }

    private static List<string> LoadTargetCharacters(string connectionString, CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CharacterId))
        {
            return new List<string> { options.CharacterId.Trim().ToLowerInvariant() };
        }

        List<string> result = new List<string>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlCommand command = new NpgsqlCommand("SELECT DISTINCT LOWER(COALESCE(character_id, '')) FROM archetypes WHERE COALESCE(character_id, '') <> '' ORDER BY 1;", connection);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static List<ArchetypeRow> LoadArchetypes(string connectionString, string characterId)
    {
        List<ArchetypeRow> rows = new List<ArchetypeRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT id, COALESCE(name, ''), COALESCE(description, ''), COALESCE(key_effect_tags, ARRAY[]::text[])
FROM archetypes
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id)
ORDER BY id;", connection);

        command.Parameters.AddWithValue("character_id", characterId);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new ArchetypeRow
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                KeyEffectTags = reader.GetFieldValue<string[]>(3)
            });
        }

        return rows;
    }

    private static List<EntityPromptRow> LoadEntities(string connectionString, string characterId)
    {
        List<EntityPromptRow> rows = new List<EntityPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        using (NpgsqlCommand cards = new NpgsqlCommand(@"
SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[])
FROM cards
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id) OR character_id IS NULL
ORDER BY id;", connection))
        {
            cards.Parameters.AddWithValue("character_id", characterId);
            using NpgsqlDataReader reader = cards.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EntityPromptRow
                {
                    EntityType = "card",
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    EffectTags = reader.GetFieldValue<string[]>(3)
                });
            }
        }

        using (NpgsqlCommand relics = new NpgsqlCommand(@"
SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[])
FROM relics
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id) OR character_id IS NULL
ORDER BY id;", connection))
        {
            relics.Parameters.AddWithValue("character_id", characterId);
            using NpgsqlDataReader reader = relics.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EntityPromptRow
                {
                    EntityType = "relic",
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    EffectTags = reader.GetFieldValue<string[]>(3)
                });
            }
        }

        using (NpgsqlCommand potions = new NpgsqlCommand(@"
SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[])
FROM potions
WHERE LOWER(COALESCE(character_id, '')) = LOWER(@character_id) OR character_id IS NULL
ORDER BY id;", connection))
        {
            potions.Parameters.AddWithValue("character_id", characterId);
            using NpgsqlDataReader reader = potions.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EntityPromptRow
                {
                    EntityType = "potion",
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    EffectTags = reader.GetFieldValue<string[]>(3)
                });
            }
        }

        return rows;
    }

    private static List<EntityPromptRow> LoadEntitiesByPool(string connectionString, string rootPath, string characterId)
    {
        List<EntityPromptRow> rows = new List<EntityPromptRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        LoadPoolEntities(connection, rows, "card", "cards", rootPath, characterId,
            () => CharacterPoolReader.ReadCardClassNames(rootPath, characterId));
        LoadPoolEntities(connection, rows, "relic", "relics", rootPath, characterId,
            () => CharacterPoolReader.ReadRelicClassNames(rootPath, characterId));
        LoadPoolEntities(connection, rows, "potion", "potions", rootPath, characterId,
            () => CharacterPoolReader.ReadPotionClassNames(rootPath, characterId));

        return rows;
    }

    private static void LoadPoolEntities(
        NpgsqlConnection connection,
        List<EntityPromptRow> rows,
        string entityType,
        string tableName,
        string rootPath,
        string characterId,
        Func<HashSet<string>> poolReader)
    {
        HashSet<string> classNames;
        try
        {
            classNames = poolReader();
        }
        catch (FileNotFoundException)
        {
            return;
        }

        if (classNames.Count == 0)
        {
            return;
        }

        string sql = "SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[]) FROM " + tableName + " WHERE id = ANY(@ids) ORDER BY id;";
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("ids", classNames.ToArray());
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EntityPromptRow
            {
                EntityType = entityType,
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Description = reader.GetString(2),
                EffectTags = reader.GetFieldValue<string[]>(3)
            });
        }
    }

    private static Dictionary<string, Dictionary<string, int>> ScoreBatch(
        ILlmProvider provider,
        string model,
        string characterId,
        IReadOnlyList<ArchetypeRow> archetypes,
        IReadOnlyList<EntityPromptRow> entities,
        int maxTokens)
    {
        StringBuilder prompt = new StringBuilder();
        prompt.AppendLine("You are an expert Slay the Spire 2 card evaluator.");
        prompt.AppendLine("Rate affinity (0-10) for each entity against each archetype.");
        prompt.AppendLine("0 = irrelevant or anti-synergistic, 5 = generally useful, 10 = core card/entity for that archetype.");
        prompt.AppendLine("Return a JSON array only. Each item must be:");
        prompt.AppendLine("{ \"entity_id\": string, \"entity_type\": string, \"affinities\": { \"archetype_name\": score } }");
        prompt.AppendLine();
        prompt.AppendLine("Character:");
        prompt.AppendLine(characterId);
        prompt.AppendLine();
        prompt.AppendLine("Archetypes:");
        prompt.AppendLine(JsonSerializer.Serialize(archetypes.Select(static a => new
        {
            name = a.Name,
            description = a.Description,
            key_effect_tags = a.KeyEffectTags
        }).ToList()));
        prompt.AppendLine();
        prompt.AppendLine("Entities:");
        prompt.AppendLine(JsonSerializer.Serialize(entities));

        int estimatedTokens = 1200 + (entities.Count * Math.Max(archetypes.Count, 1) * 8);
        int requestMaxTokens = Math.Max(maxTokens, Math.Min(estimatedTokens, 8000));

        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            SystemPrompt = "You are an expert Slay the Spire 2 evaluator. Return only valid JSON.",
            Prompt = prompt.ToString(),
            MaxTokens = requestMaxTokens
        };

        AnnotationResult result = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return ParseScores(result.Content);
    }

    private static Dictionary<string, Dictionary<string, int>> ParseScores(string content)
    {
        Dictionary<string, Dictionary<string, int>> result = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        string cleaned = CleanJson(content);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(cleaned);
        }
        catch (JsonException)
        {
            return result;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                string entityId = ReadString(element, "entity_id");
                if (string.IsNullOrWhiteSpace(entityId))
                {
                    entityId = ReadString(element, "card_id");
                }

                if (string.IsNullOrWhiteSpace(entityId))
                {
                    continue;
                }

                if (!element.TryGetProperty("affinities", out JsonElement affinities) || affinities.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in affinities.EnumerateObject())
                {
                    int parsed = ParseScore(property.Value);
                    values[property.Name] = Math.Max(0, Math.Min(10, parsed));
                }

                result[entityId] = values;
            }
        }

        return result;
    }

    private static int ParseScore(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out int i))
            {
                return i;
            }

            if (value.TryGetDouble(out double d))
            {
                return (int)Math.Round(d, MidpointRounding.AwayFromZero);
            }
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            string raw = value.GetString() ?? string.Empty;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
            {
                return parsedInt;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble))
            {
                return (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
            }
        }

        return 0;
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return property.GetString() ?? string.Empty;
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

    private static void UpsertRows(string connectionString, IReadOnlyList<EntityArchetypeAffinityRecord> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction();

        HashSet<int> archetypeIds = new HashSet<int>(rows.Select(static r => r.ArchetypeId));
        using (NpgsqlCommand delete = new NpgsqlCommand("DELETE FROM entity_archetype_affinity WHERE archetype_id = ANY(@ids);", connection, transaction))
        {
            delete.Parameters.AddWithValue("ids", archetypeIds.ToArray());
            delete.ExecuteNonQuery();
        }

        for (int i = 0; i < rows.Count; i++)
        {
            EntityArchetypeAffinityRecord row = rows[i];
            using NpgsqlCommand insert = new NpgsqlCommand(@"
INSERT INTO entity_archetype_affinity (archetype_id, entity_type, entity_id, affinity_score)
VALUES (@archetype_id, @entity_type, @entity_id, @affinity_score)
ON CONFLICT (archetype_id, entity_type, entity_id)
DO UPDATE SET affinity_score = EXCLUDED.affinity_score;", connection, transaction);

            insert.Parameters.AddWithValue("archetype_id", row.ArchetypeId);
            insert.Parameters.AddWithValue("entity_type", row.EntityType);
            insert.Parameters.AddWithValue("entity_id", row.EntityId);
            insert.Parameters.AddWithValue("affinity_score", row.AffinityScore);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private sealed class ArchetypeRow
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string[] KeyEffectTags { get; set; }

        public ArchetypeRow()
        {
            Name = string.Empty;
            Description = string.Empty;
            KeyEffectTags = Array.Empty<string>();
        }
    }

    private sealed class EntityPromptRow
    {
        public string EntityType { get; set; }

        public string Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public string[] EffectTags { get; set; }

        public EntityPromptRow()
        {
            EntityType = string.Empty;
            Id = string.Empty;
            Title = string.Empty;
            Description = string.Empty;
            EffectTags = Array.Empty<string>();
        }
    }
}

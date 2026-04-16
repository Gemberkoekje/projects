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
    private const int MaxRequestedAffinitiesPerBatch = 64;
    private const int BatchEntitySummaryLimit = 6;

    public ScoreAffinitiesResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string stagingDirectory = Path.GetDirectoryName(options.OutputPath) ?? options.RootPath;
        CheckpointManager checkpoint = new CheckpointManager(stagingDirectory, "score-affinities");
        
        CheckpointRecord resumeFrom = null;
        if (!string.IsNullOrWhiteSpace(options.ResumeCharacterId) && options.ResumeBatchNumber > 0)
        {
            // Resume from explicit command-line parameters
            resumeFrom = new CheckpointRecord
            {
                CharacterId = options.ResumeCharacterId,
                BatchNumber = options.ResumeBatchNumber
            };
            Console.WriteLine($"[score-affinities] Resuming from command-line: character={options.ResumeCharacterId}, batch={options.ResumeBatchNumber}");
        }
        else if (options.Resume)
        {
            // Resume from checkpoint file
            resumeFrom = checkpoint.LoadCheckpoint();
            if (resumeFrom is not null)
            {
                Console.WriteLine($"[score-affinities] Resuming from checkpoint: character={resumeFrom.CharacterId}, batch={resumeFrom.BatchNumber}/{resumeFrom.TotalBatches}");
            }
        }

        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.AffinityScoring)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options, model);
        List<string> characters = LoadTargetCharacters(connectionString, options);
        List<EntityArchetypeAffinityRecord> allRows = new List<EntityArchetypeAffinityRecord>();

        int totalEntities = 0;
        int batchCount = 0;
        bool hasResumedFromCheckpoint = false;

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

            List<EntityScoringRow> scoringRows = BuildScoringRows(connectionString, characterId, archetypes, entities, options.OnlyMissingAffinities);
            if (scoringRows.Count == 0)
            {
                Console.WriteLine($"[score-affinities] ({i + 1}/{characters.Count}) {characterId}: no missing affinities to score");
                continue;
            }

            totalEntities += scoringRows.Count;

            int batchSize = GetBatchSize(options, archetypes.Count);
            int totalCharacterBatches = (int)Math.Ceiling(scoringRows.Count / (double)batchSize);
            int totalRequestedAffinities = scoringRows.Sum(static row => row.Archetypes.Count);
            Console.WriteLine($"[score-affinities] ({i + 1}/{characters.Count}) {characterId}: {scoringRows.Count} entities to score across {totalCharacterBatches} batches (batch size {batchSize}, archetypes {archetypes.Count}, requested affinities {totalRequestedAffinities})");

            if (!options.OnlyMissingAffinities)
            {
                DeleteCharacterAffinities(connectionString, archetypes);
            }

            int startBatchNumber = 1;
            if (!hasResumedFromCheckpoint && resumeFrom is not null && resumeFrom.CharacterId == characterId)
            {
                startBatchNumber = resumeFrom.BatchNumber;
                hasResumedFromCheckpoint = true;
                Console.WriteLine($"[score-affinities] Resuming character {characterId} from batch {startBatchNumber}/{totalCharacterBatches}");
            }

            for (int start = 0; start < scoringRows.Count; start += batchSize)
            {
                List<EntityScoringRow> batch = scoringRows.Skip(start).Take(batchSize).ToList();
                int batchNumber = (start / batchSize) + 1;

                if (batchNumber < startBatchNumber)
                {
                    batchCount++;
                    continue;
                }

                int batchStartEntity = start + 1;
                int batchEndEntity = start + batch.Count;
                int remainingBeforeBatch = scoringRows.Count - start;
                int remainingAfterBatch = scoringRows.Count - batchEndEntity;
                int requestedAffinities = batch.Sum(static row => row.Archetypes.Count);
                string batchLabel = $"[score-affinities] ({i + 1}/{characters.Count}) {characterId} batch {batchNumber}/{totalCharacterBatches}";
                string entitySummary = DescribeBatchEntities(batch, BatchEntitySummaryLimit);

                Console.WriteLine($"{batchLabel}: entities {batchStartEntity}-{batchEndEntity}/{scoringRows.Count} ({remainingBeforeBatch} remaining incl. current, {remainingAfterBatch} after); requested affinities {requestedAffinities}; set {entitySummary}");

                Dictionary<string, Dictionary<string, int>> scored = ScoreBatch(provider, model, characterId, batch, options.MaxTokens, batchLabel);

                int affinitiesAdded = 0;
                List<EntityArchetypeAffinityRecord> batchRows = new List<EntityArchetypeAffinityRecord>();
                for (int b = 0; b < batch.Count; b++)
                {
                    EntityScoringRow scoringRow = batch[b];
                    if (!scored.TryGetValue(scoringRow.Entity.Id, out Dictionary<string, int> entityAffinities))
                    {
                        continue;
                    }

                    for (int a = 0; a < scoringRow.Archetypes.Count; a++)
                    {
                        ArchetypeRow archetype = scoringRow.Archetypes[a];
                        if (!TryGetAffinityScore(entityAffinities, archetype.Name, out int value))
                        {
                            if (options.OnlyMissingAffinities)
                            {
                                continue;
                            }

                            value = 0;
                        }

                        int affinity = Math.Max(0, Math.Min(10, value));
                        batchRows.Add(new EntityArchetypeAffinityRecord
                        {
                            ArchetypeId = archetype.Id,
                            CharacterId = characterId,
                            ArchetypeName = archetype.Name,
                            EntityType = scoringRow.Entity.EntityType,
                            EntityId = scoringRow.Entity.Id,
                            AffinityScore = affinity
                        });
                        affinitiesAdded++;
                    }
                }

                UpsertBatchRows(connectionString, batchRows);
                allRows.AddRange(batchRows);

                Console.WriteLine($"{batchLabel}: completed with {scored.Count}/{batch.Count} entity responses and {affinitiesAdded} affinity rows written; {remainingAfterBatch} entities remain for {characterId}");
                checkpoint.SaveCheckpoint(characterId, batchNumber, totalCharacterBatches);
                batchCount++;
            }
        }

        checkpoint.ClearCheckpoint();

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

    private static int GetBatchSize(CliOptions options, int archetypeCount)
    {
        int limitedArchetypeCount = Math.Max(archetypeCount, 1);
        int maxByAffinityCount = Math.Max(1, MaxRequestedAffinitiesPerBatch / limitedArchetypeCount);
        return Math.Max(1, Math.Min(options.BatchSize, Math.Min(MaxAffinityBatchSize, maxByAffinityCount)));
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
SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[]), COALESCE(keywords, ARRAY[]::text[])
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
                    EffectTags = reader.GetFieldValue<string[]>(3),
                    Keywords = reader.GetFieldValue<string[]>(4)
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
                    EffectTags = reader.GetFieldValue<string[]>(3),
                    Keywords = Array.Empty<string>()
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
                    EffectTags = reader.GetFieldValue<string[]>(3),
                    Keywords = Array.Empty<string>()
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

        string sql = string.Equals(entityType, "card", StringComparison.OrdinalIgnoreCase)
            ? "SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[]), COALESCE(keywords, ARRAY[]::text[]) FROM " + tableName + " WHERE id = ANY(@ids) ORDER BY id;"
            : "SELECT id, COALESCE(title, id), COALESCE(description, ''), COALESCE(effect_tags, ARRAY[]::text[]), ARRAY[]::text[] FROM " + tableName + " WHERE id = ANY(@ids) ORDER BY id;";

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
                EffectTags = reader.GetFieldValue<string[]>(3),
                Keywords = reader.GetFieldValue<string[]>(4)
            });
        }
    }

    private static List<EntityScoringRow> BuildScoringRows(
        string connectionString,
        string characterId,
        IReadOnlyList<ArchetypeRow> archetypes,
        IReadOnlyList<EntityPromptRow> entities,
        bool onlyMissingAffinities)
    {
        List<EntityScoringRow> rows = new List<EntityScoringRow>();
        if (!onlyMissingAffinities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                rows.Add(new EntityScoringRow
                {
                    Entity = entities[i],
                    Archetypes = archetypes.ToList()
                });
            }

            return rows;
        }

        HashSet<string> existingKeys = LoadExistingAffinityKeys(connectionString, characterId);
        for (int i = 0; i < entities.Count; i++)
        {
            EntityPromptRow entity = entities[i];
            List<ArchetypeRow> missingArchetypes = new List<ArchetypeRow>();
            for (int a = 0; a < archetypes.Count; a++)
            {
                ArchetypeRow archetype = archetypes[a];
                if (!existingKeys.Contains(BuildAffinityKey(archetype.Id, entity.EntityType, entity.Id)))
                {
                    missingArchetypes.Add(archetype);
                }
            }

            if (missingArchetypes.Count > 0)
            {
                rows.Add(new EntityScoringRow
                {
                    Entity = entity,
                    Archetypes = missingArchetypes
                });
            }
        }

        return rows;
    }

    private static HashSet<string> LoadExistingAffinityKeys(string connectionString, string characterId)
    {
        HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlCommand command = new NpgsqlCommand(@"
SELECT eaa.archetype_id, eaa.entity_type, eaa.entity_id
FROM entity_archetype_affinity eaa
INNER JOIN archetypes a ON a.id = eaa.archetype_id
WHERE LOWER(COALESCE(a.character_id, '')) = LOWER(@character_id);", connection);
        command.Parameters.AddWithValue("character_id", characterId);

        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            keys.Add(BuildAffinityKey(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return keys;
    }

    private static string BuildAffinityKey(int archetypeId, string entityType, string entityId)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", archetypeId, entityType, entityId);
    }

    private static Dictionary<string, Dictionary<string, int>> ScoreBatch(
        ILlmProvider provider,
        string model,
        string characterId,
        IReadOnlyList<EntityScoringRow> entities,
        int maxTokens,
        string batchLabel)
    {
        StringBuilder prompt = new StringBuilder();
        prompt.AppendLine("You are an expert Slay the Spire 2 card evaluator.");
        prompt.AppendLine("Rate affinity (0-10) for each entity against the requested archetypes only.");
        prompt.AppendLine("0 = irrelevant or anti-synergistic, 5 = generally useful, 10 = core card/entity for that archetype.");
        prompt.AppendLine("Return a JSON array only. Each item must be:");
        prompt.AppendLine("{ \"entity_id\": string, \"entity_type\": string, \"affinities\": { \"archetype_name\": score } }");
        prompt.AppendLine("Only include affinities for archetypes listed in target_archetypes for that entity.");
        prompt.AppendLine("Use the exact archetype names provided, preserving spaces and capitalization.");
        prompt.AppendLine("Do not include analysis, explanations, or extra fields.");
        prompt.AppendLine();
        prompt.AppendLine("Character:");
        prompt.AppendLine(characterId);
        prompt.AppendLine();
        prompt.AppendLine("Archetypes:");
        prompt.AppendLine(JsonSerializer.Serialize(entities
            .SelectMany(static row => row.Archetypes)
            .GroupBy(static archetype => archetype.Id)
            .Select(static group => new
            {
                name = group.First().Name,
                description = group.First().Description,
                key_effect_tags = group.First().KeyEffectTags
            })
            .ToList()));
        prompt.AppendLine();
        prompt.AppendLine("Entities:");
        prompt.AppendLine(JsonSerializer.Serialize(entities.Select(static row => new
        {
            entity_id = row.Entity.Id,
            entity_type = row.Entity.EntityType,
            title = row.Entity.Title,
            description = row.Entity.Description,
            keywords = row.Entity.Keywords,
            effect_tags = row.Entity.EffectTags,
            target_archetypes = row.Archetypes.Select(static archetype => archetype.Name).ToArray()
        }).ToList()));

        int requestedAffinityCount = entities.Sum(static row => row.Archetypes.Count);
        int estimatedTokens = 1200 + (requestedAffinityCount * 8);
        int requestMaxTokens = Math.Max(maxTokens, Math.Min(estimatedTokens, 8000));

        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            SystemPrompt = "You are an expert Slay the Spire 2 evaluator. Return only valid JSON.",
            Prompt = prompt.ToString(),
            MaxTokens = requestMaxTokens
        };

        Console.WriteLine($"{batchLabel}: sending request for {entities.Count} entities / {requestedAffinityCount} affinities (max tokens {requestMaxTokens})");
        AnnotationResult result = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        Dictionary<string, Dictionary<string, int>> parsed = ParseScores(result.Content);
        HashSet<string> expectedEntityIds = new HashSet<string>(entities.Select(static row => row.Entity.Id), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Dictionary<string, int>> filtered = parsed
            .Where(pair => expectedEntityIds.Contains(pair.Key))
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        if (filtered.Count == entities.Count || entities.Count <= 1)
        {
            return filtered;
        }

        List<EntityScoringRow> missing = entities
            .Where(row => !filtered.ContainsKey(row.Entity.Id))
            .ToList();

        Console.WriteLine($"{batchLabel}: retrying {missing.Count} missing entities with smaller batches; missing set {DescribeBatchEntities(missing, BatchEntitySummaryLimit)}");

        if (missing.Count == entities.Count)
        {
            int midpoint = entities.Count / 2;
            Dictionary<string, Dictionary<string, int>> left = ScoreBatch(provider, model, characterId, entities.Take(midpoint).ToList(), maxTokens, batchLabel + " [split-1]");
            Dictionary<string, Dictionary<string, int>> right = ScoreBatch(provider, model, characterId, entities.Skip(midpoint).ToList(), maxTokens, batchLabel + " [split-2]");
            foreach (KeyValuePair<string, Dictionary<string, int>> pair in right)
            {
                left[pair.Key] = pair.Value;
            }

            return left;
        }

        Dictionary<string, Dictionary<string, int>> recovered = ScoreBatch(provider, model, characterId, missing, maxTokens, batchLabel + " [retry]");
        foreach (KeyValuePair<string, Dictionary<string, int>> pair in recovered)
        {
            filtered[pair.Key] = pair.Value;
        }

        return filtered;
    }

    private static string DescribeBatchEntities(IReadOnlyList<EntityScoringRow> entities, int maxItems)
    {
        if (entities.Count == 0)
        {
            return "(none)";
        }

        string typeBreakdown = string.Join(", ",
            entities
                .GroupBy(static row => row.Entity.EntityType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Key + ":" + group.Count().ToString(CultureInfo.InvariantCulture)));

        List<string> sample = entities
            .Take(Math.Max(maxItems, 1))
            .Select(static row => row.Entity.EntityType + ":" + row.Entity.Id)
            .ToList();

        string sampleText = string.Join(", ", sample);
        if (entities.Count > sample.Count)
        {
            sampleText += string.Format(CultureInfo.InvariantCulture, ", ... +{0} more", entities.Count - sample.Count);
        }

        return string.Format(CultureInfo.InvariantCulture, "[{0}] {1}", typeBreakdown, sampleText);
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
            ParseScoreNode(document.RootElement, result);
        }

        return result;
    }

    private static void ParseScoreNode(JsonElement node, Dictionary<string, Dictionary<string, int>> result)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in node.EnumerateArray())
            {
                if (TryReadEntityAffinities(element, out string entityId, out Dictionary<string, int> values))
                {
                    result[entityId] = values;
                }
                else
                {
                    ParseScoreNode(element, result);
                }
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (TryReadEntityAffinities(node, out string singleEntityId, out Dictionary<string, int> singleValues))
        {
            result[singleEntityId] = singleValues;
            return;
        }

        foreach (JsonProperty property in node.EnumerateObject())
        {
            if (IsContainerProperty(property.Name))
            {
                ParseScoreNode(property.Value, result);
                continue;
            }

            if (TryReadEntityAffinities(property.Name, property.Value, out string entityId, out Dictionary<string, int> values))
            {
                result[entityId] = values;
            }
        }
    }

    private static bool TryReadEntityAffinities(JsonElement element, out string entityId, out Dictionary<string, int> values)
    {
        entityId = ReadFirstString(element, "entity_id", "card_id", "relic_id", "potion_id", "id", "entity", "card", "relic", "potion", "name", "title");
        if (string.IsNullOrWhiteSpace(entityId))
        {
            values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return false;
        }

        return TryReadAffinityValues(element, out values);
    }

    private static bool TryReadEntityAffinities(string propertyName, JsonElement element, out string entityId, out Dictionary<string, int> values)
    {
        entityId = propertyName;
        return TryReadAffinityValues(element, out values);
    }

    private static bool TryReadAffinityValues(JsonElement element, out Dictionary<string, int> values)
    {
        values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryGetObjectProperty(element, out JsonElement affinityNode, "affinities", "scores", "archetypes"))
        {
            return ReadAffinityMap(affinityNode, values);
        }

        return false;
    }

    private static bool TryGetObjectProperty(JsonElement element, out JsonElement propertyValue, params string[] names)
    {
        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out propertyValue) && propertyValue.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
        }

        propertyValue = default;
        return false;
    }

    private static bool ReadAffinityMap(JsonElement affinityNode, Dictionary<string, int> values)
    {
        if (affinityNode.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (JsonProperty property in affinityNode.EnumerateObject())
        {
            int parsed = ParseScore(property.Value);
            values[property.Name] = Math.Max(0, Math.Min(10, parsed));
        }

        return values.Count > 0;
    }

    private static string ReadFirstString(JsonElement element, params string[] names)
    {
        for (int i = 0; i < names.Length; i++)
        {
            string value = ReadString(element, names[i]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool IsContainerProperty(string name)
    {
        return string.Equals(name, "items", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "results", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "entities", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "output", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetAffinityScore(Dictionary<string, int> entityAffinities, string archetypeName, out int value)
    {
        if (entityAffinities.TryGetValue(archetypeName, out value))
        {
            return true;
        }

        string normalizedTarget = NormalizeAffinityKey(archetypeName);
        foreach (KeyValuePair<string, int> pair in entityAffinities)
        {
            if (string.Equals(NormalizeAffinityKey(pair.Key), normalizedTarget, StringComparison.Ordinal))
            {
                value = pair.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static string NormalizeAffinityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
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

    private static void DeleteCharacterAffinities(string connectionString, IReadOnlyList<ArchetypeRow> archetypes)
    {
        if (archetypes.Count == 0)
        {
            return;
        }

        int[] archetypeIds = archetypes.Select(static a => a.Id).ToArray();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlCommand delete = new NpgsqlCommand("DELETE FROM entity_archetype_affinity WHERE archetype_id = ANY(@ids);", connection);
        delete.Parameters.AddWithValue("ids", archetypeIds);
        delete.ExecuteNonQuery();
    }

    private static void UpsertBatchRows(string connectionString, IReadOnlyList<EntityArchetypeAffinityRecord> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using NpgsqlTransaction transaction = connection.BeginTransaction();

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

        public string[] Keywords { get; set; }

        public EntityPromptRow()
        {
            EntityType = string.Empty;
            Id = string.Empty;
            Title = string.Empty;
            Description = string.Empty;
            EffectTags = Array.Empty<string>();
            Keywords = Array.Empty<string>();
        }
    }

    private sealed class EntityScoringRow
    {
        public EntityPromptRow Entity { get; set; }

        public List<ArchetypeRow> Archetypes { get; set; }

        public EntityScoringRow()
        {
            Entity = new EntityPromptRow();
            Archetypes = new List<ArchetypeRow>();
        }
    }
}

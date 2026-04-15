using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Sts2Extractor.Annotation;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Npgsql;

namespace Sts2Extractor.Extractors;

internal sealed class BuildSynergyEdgesRunner
{
    public BuildSynergyEdgesResult Run(CliOptions options)
    {
        string connectionString = DatabaseSettingsResolver.ResolveConnectionString(options);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing PostgreSQL connection string. Use --connection-string, Database:ConnectionString, or STS2_POSTGRES_CONNECTION_STRING.");
        }

        string outputDirectory = Path.GetDirectoryName(options.OutputPath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string progressPath = options.OutputPath + ".progress.csv";

        Console.WriteLine("(1/4) Loading entities");
        List<EntityTagRow> entities = LoadEntities(connectionString);

        Console.WriteLine("(2/4) Building candidate pairs");
        List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> candidates = BuildCandidatePairs(entities);

        Console.WriteLine("(3/4) Preparing annotation batches");
        if (candidates.Count == 0)
        {
            CsvWriter.Write(options.OutputPath, SynergyEdgeCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
            CsvWriter.Write(progressPath, ProgressCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
            Console.WriteLine("(4/4) No candidates found; wrote empty output");
            return new BuildSynergyEdgesResult
            {
                OutputPath = options.OutputPath,
                EdgeCount = 0,
                CandidatePairCount = 0,
                BatchCount = 0
            };
        }

        List<SynergyEdgeRecord> allEdges = new List<SynergyEdgeRecord>();
        HashSet<string> processedPairKeys = new HashSet<string>(StringComparer.Ordinal);

        if (options.Resume)
        {
            allEdges = LoadExistingEdges(options.OutputPath);
            processedPairKeys = LoadProcessedPairs(progressPath, allEdges);
            if (!File.Exists(options.OutputPath))
            {
                CsvWriter.Write(options.OutputPath, SynergyEdgeCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
            }

            if (!File.Exists(progressPath))
            {
                CsvWriter.Write(progressPath, ProgressCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
            }

            Console.WriteLine($"[synergy] Resume mode enabled. Loaded {allEdges.Count} prior edges and {processedPairKeys.Count} processed pairs.");
        }
        else
        {
            CsvWriter.Write(options.OutputPath, SynergyEdgeCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
            CsvWriter.Write(progressPath, ProgressCsvColumns(), Enumerable.Empty<IReadOnlyList<string>>());
        }

        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.Synergy)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options.Provider);

        int batchSize = options.BatchSize;
        int batchCount = 0;
        int totalCandidates = candidates.Count;

        for (int start = 0; start < candidates.Count; start += batchSize)
        {
            List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> batch = candidates
                .Skip(start)
                .Take(batchSize)
                .ToList();

            List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> pendingBatch = batch
                .Where(p => !processedPairKeys.Contains(BuildPairKey(p.A.EntityType, p.A.EntityId, p.B.EntityType, p.B.EntityId)))
                .ToList();

            int processed = Math.Min(start + batch.Count, totalCandidates);
            if (pendingBatch.Count == 0)
            {
                Console.WriteLine($"({processed}/{totalCandidates}) Skipping already-processed synergy pairs");
                continue;
            }

            Console.WriteLine($"({processed}/{totalCandidates}) Annotating synergy pairs");

            List<SynergyEdgeRecord> batchEdges = AnnotateBatch(pendingBatch, provider, model, options.MaxTokens);
            allEdges.AddRange(batchEdges);
            AppendEdges(options.OutputPath, batchEdges);

            AppendProgress(progressPath, pendingBatch);
            for (int i = 0; i < pendingBatch.Count; i++)
            {
                (EntityTagRow a, EntityTagRow b, _) = pendingBatch[i];
                processedPairKeys.Add(BuildPairKey(a.EntityType, a.EntityId, b.EntityType, b.EntityId));
            }

            batchCount++;
        }

        Console.WriteLine("(4/4) Writing synergy edge output");

        return new BuildSynergyEdgesResult
        {
            OutputPath = options.OutputPath,
            EdgeCount = allEdges.Count,
            CandidatePairCount = candidates.Count,
            BatchCount = batchCount
        };
    }

    private static string[] SynergyEdgeCsvColumns()
    {
        return new[]
        {
            "entity_a_type",
            "entity_a_id",
            "entity_b_type",
            "entity_b_id",
            "synergy_strength",
            "is_anti_synergy",
            "shared_tags",
            "explanation"
        };
    }

    private static string[] ProgressCsvColumns()
    {
        return new[]
        {
            "entity_a_type",
            "entity_a_id",
            "entity_b_type",
            "entity_b_id"
        };
    }

    private static List<EntityTagRow> LoadEntities(string connectionString)
    {
        List<EntityTagRow> result = new List<EntityTagRow>();
        using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        connection.Open();

        LoadEntityRows(connection, result, "card",
            "SELECT id, effect_tags, resources_generated, resources_consumed, anti_synergy_tags, ARRAY[]::text[] FROM cards ORDER BY id");
        LoadEntityRows(connection, result, "relic",
            "SELECT id, effect_tags, resources_generated, resources_consumed, ARRAY[]::text[], triggers_on FROM relics ORDER BY id");
        LoadEntityRows(connection, result, "potion",
            "SELECT id, effect_tags, ARRAY[]::text[], ARRAY[]::text[], ARRAY[]::text[], ARRAY[]::text[] FROM potions ORDER BY id");

        return result;
    }

    private static void LoadEntityRows(NpgsqlConnection connection, List<EntityTagRow> result, string entityType, string sql)
    {
        using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        using NpgsqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            string[] effectTags = ReadStringArray(reader, 1);
            string[] resourcesGenerated = ReadStringArray(reader, 2);
            string[] resourcesConsumed = ReadStringArray(reader, 3);
            string[] antiSynergyTags = ReadStringArray(reader, 4);
            string[] triggersOn = ReadStringArray(reader, 5);

            HashSet<string> allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < effectTags.Length; i++) allTags.Add(effectTags[i]);
            for (int i = 0; i < resourcesGenerated.Length; i++) allTags.Add(resourcesGenerated[i]);
            for (int i = 0; i < resourcesConsumed.Length; i++) allTags.Add(resourcesConsumed[i]);
            for (int i = 0; i < triggersOn.Length; i++) allTags.Add(triggersOn[i]);

            result.Add(new EntityTagRow
            {
                EntityType = entityType,
                EntityId = id,
                AllTags = allTags,
                AntiSynergyTags = new HashSet<string>(antiSynergyTags, StringComparer.OrdinalIgnoreCase)
            });
        }
    }

    private static string[] ReadStringArray(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return Array.Empty<string>();
        }

        string[] value = reader.GetFieldValue<string[]>(ordinal);
        return value ?? Array.Empty<string>();
    }

    private static List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> BuildCandidatePairs(List<EntityTagRow> entities)
    {
        List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> result = new List<(EntityTagRow, EntityTagRow, string[])>();

        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                EntityTagRow a = entities[i];
                EntityTagRow b = entities[j];

                string[] shared = a.AllTags
                    .Where(t => b.AllTags.Contains(t))
                    .OrderBy(static t => t, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (shared.Length > 0)
                {
                    result.Add((a, b, shared));
                }
            }
        }

        return result;
    }

    private static List<SynergyEdgeRecord> AnnotateBatch(
        List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> batch,
        ILlmProvider provider,
        string model,
        int maxTokens)
    {
        StringBuilder promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are an expert Slay the Spire 2 synergy analyst. For each pair below, output a JSON array where each element has:");
        promptBuilder.AppendLine("  entity_a_type, entity_a_id, entity_b_type, entity_b_id,");
        promptBuilder.AppendLine("  synergy_strength (int 1-10), is_anti_synergy (bool),");
        promptBuilder.AppendLine("  direction (\"a_to_b\" | \"b_to_a\" | \"both\"),");
        promptBuilder.AppendLine("  shared_tags (semicolon-separated string), explanation (one sentence).");
        promptBuilder.AppendLine("Only include pairs with synergy_strength >= 4. Omit neutral/weak pairs.");
        promptBuilder.AppendLine("Return only a JSON array, no other text.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Pairs:");

        for (int i = 0; i < batch.Count; i++)
        {
            (EntityTagRow a, EntityTagRow b, string[] sharedTags) = batch[i];
            promptBuilder.AppendLine($"- {a.EntityType}:{a.EntityId} <-> {b.EntityType}:{b.EntityId} shared=[{string.Join(",", sharedTags)}]");
        }

        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            SystemPrompt = "You are an expert Slay the Spire 2 synergy analyst. Return compact JSON only.",
            Prompt = promptBuilder.ToString(),
            MaxTokens = maxTokens
        };

        AnnotationResult annotationResult = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return ParseEdgesFromJson(annotationResult.Content, batch);
    }

    private static List<SynergyEdgeRecord> ParseEdgesFromJson(
        string json,
        List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> batch)
    {
        List<SynergyEdgeRecord> edges = new List<SynergyEdgeRecord>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return edges;
        }

        int arrayStart = json.IndexOf('[');
        int arrayEnd = json.LastIndexOf(']');
        if (arrayStart < 0 || arrayEnd <= arrayStart)
        {
            return edges;
        }

        string arrayJson = json[arrayStart..(arrayEnd + 1)];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(arrayJson);
        }
        catch (JsonException)
        {
            return edges;
        }

        using (document)
        {
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                SynergyEdgeRecord record = TryParseEdge(element, batch);
                if (record != null)
                {
                    edges.Add(record);
                }
            }
        }

        return edges;
    }

    private static SynergyEdgeRecord TryParseEdge(
        JsonElement element,
        List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> batch)
    {
        string entityAType = ReadString(element, "entity_a_type");
        string entityAId = ReadString(element, "entity_a_id");
        string entityBType = ReadString(element, "entity_b_type");
        string entityBId = ReadString(element, "entity_b_id");

        if (string.IsNullOrWhiteSpace(entityAId) || string.IsNullOrWhiteSpace(entityBId))
        {
            return null;
        }

        if (!element.TryGetProperty("synergy_strength", out JsonElement strengthNode)
            || !strengthNode.TryGetInt32(out int strength))
        {
            return null;
        }

        if (strength < 1 || strength > 10)
        {
            return null;
        }

        bool isAnti = false;
        if (element.TryGetProperty("is_anti_synergy", out JsonElement antiNode))
        {
            isAnti = antiNode.ValueKind == JsonValueKind.True;
        }

        string direction = ReadString(element, "direction");
        string sharedTags = ReadString(element, "shared_tags");
        string explanation = ReadString(element, "explanation");

        if (string.IsNullOrWhiteSpace(sharedTags))
        {
            (EntityTagRow A, EntityTagRow B, string[] SharedTags) pair = batch.FirstOrDefault(p =>
                string.Equals(p.A.EntityId, entityAId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.B.EntityId, entityBId, StringComparison.OrdinalIgnoreCase));
            if (pair.SharedTags != null)
            {
                sharedTags = string.Join(";", pair.SharedTags);
            }
        }

        if (string.Equals(direction, "b_to_a", StringComparison.OrdinalIgnoreCase))
        {
            (entityAType, entityBType) = (entityBType, entityAType);
            (entityAId, entityBId) = (entityBId, entityAId);
        }

        return new SynergyEdgeRecord
        {
            EntityAType = entityAType,
            EntityAId = entityAId,
            EntityBType = entityBType,
            EntityBId = entityBId,
            SynergyStrength = strength,
            IsAntiSynergy = isAnti,
            SharedTags = sharedTags,
            Explanation = explanation
        };
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement node) && node.ValueKind == JsonValueKind.String)
        {
            return node.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static List<SynergyEdgeRecord> LoadExistingEdges(string path)
    {
        List<SynergyEdgeRecord> edges = new List<SynergyEdgeRecord>();
        if (!File.Exists(path))
        {
            return edges;
        }

        List<Dictionary<string, string>> rows = CsvReader.ReadRows(path);
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            if (!TryParseInt(Read(row, "synergy_strength"), out int strength))
            {
                continue;
            }

            edges.Add(new SynergyEdgeRecord
            {
                EntityAType = Read(row, "entity_a_type"),
                EntityAId = Read(row, "entity_a_id"),
                EntityBType = Read(row, "entity_b_type"),
                EntityBId = Read(row, "entity_b_id"),
                SynergyStrength = strength,
                IsAntiSynergy = ParseBool(Read(row, "is_anti_synergy")),
                SharedTags = Read(row, "shared_tags"),
                Explanation = Read(row, "explanation")
            });
        }

        return edges;
    }

    private static HashSet<string> LoadProcessedPairs(string progressPath, List<SynergyEdgeRecord> existingEdges)
    {
        HashSet<string> processed = new HashSet<string>(StringComparer.Ordinal);

        if (File.Exists(progressPath))
        {
            List<Dictionary<string, string>> rows = CsvReader.ReadRows(progressPath);
            for (int i = 0; i < rows.Count; i++)
            {
                Dictionary<string, string> row = rows[i];
                string key = BuildPairKey(
                    Read(row, "entity_a_type"),
                    Read(row, "entity_a_id"),
                    Read(row, "entity_b_type"),
                    Read(row, "entity_b_id"));

                if (key.Length > 3)
                {
                    processed.Add(key);
                }
            }

            return processed;
        }

        for (int i = 0; i < existingEdges.Count; i++)
        {
            SynergyEdgeRecord edge = existingEdges[i];
            string key = BuildPairKey(edge.EntityAType, edge.EntityAId, edge.EntityBType, edge.EntityBId);
            if (key.Length > 3)
            {
                processed.Add(key);
            }
        }

        return processed;
    }

    private static void AppendEdges(string outputPath, List<SynergyEdgeRecord> edges)
    {
        if (edges.Count == 0)
        {
            return;
        }

        using StreamWriter writer = new StreamWriter(outputPath, true, Encoding.UTF8);
        for (int i = 0; i < edges.Count; i++)
        {
            SynergyEdgeRecord r = edges[i];
            writer.WriteLine(JoinCsv(new[]
            {
                r.EntityAType,
                r.EntityAId,
                r.EntityBType,
                r.EntityBId,
                r.SynergyStrength.ToString(CultureInfo.InvariantCulture),
                r.IsAntiSynergy ? "true" : "false",
                r.SharedTags,
                r.Explanation
            }));
        }
    }

    private static void AppendProgress(string progressPath, List<(EntityTagRow A, EntityTagRow B, string[] SharedTags)> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        using StreamWriter writer = new StreamWriter(progressPath, true, Encoding.UTF8);
        for (int i = 0; i < batch.Count; i++)
        {
            (EntityTagRow a, EntityTagRow b, _) = batch[i];
            writer.WriteLine(JoinCsv(new[]
            {
                a.EntityType,
                a.EntityId,
                b.EntityType,
                b.EntityId
            }));
        }
    }

    private static string Read(Dictionary<string, string> row, string key)
    {
        if (row.TryGetValue(key, out string value))
        {
            return value;
        }

        return string.Empty;
    }

    private static bool TryParseInt(string value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
    }

    private static bool ParseBool(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPairKey(string entityAType, string entityAId, string entityBType, string entityBId)
    {
        return string.Concat(
            entityAType.Trim(), "|",
            entityAId.Trim(), "|",
            entityBType.Trim(), "|",
            entityBId.Trim());
    }

    private static string JoinCsv(IReadOnlyList<string> values)
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(values[i]));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        string safe = value;
        if (!safe.Contains('"') && !safe.Contains(',') && !safe.Contains('\n') && !safe.Contains('\r'))
        {
            return safe;
        }

        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private sealed class EntityTagRow
    {
        public string EntityType { get; set; }

        public string EntityId { get; set; }

        public HashSet<string> AllTags { get; set; }

        public HashSet<string> AntiSynergyTags { get; set; }

        public EntityTagRow()
        {
            EntityType = string.Empty;
            EntityId = string.Empty;
            AllTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AntiSynergyTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

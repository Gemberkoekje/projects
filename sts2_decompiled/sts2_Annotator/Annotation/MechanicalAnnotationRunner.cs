using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sts2Extractor.Annotation.Providers;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Annotation;

internal sealed class MechanicalAnnotationRunner
{
    private const string MechanicalSystemPrompt =
        "You are an expert Slay the Spire 2 mechanics annotator. " +
        "Analyze source code and return only a JSON array with no markdown fencing, no extra commentary.";

    private const int BatchMaxTokens = 8192;

    private static readonly AnnotationSchema CardSchema = new AnnotationSchema(
        "card_id",
        "on_play_source",
        new[] { "effect_tags", "effect_description", "resources_generated", "resources_consumed", "scaling_type", "anti_synergy_tags" });

    private static readonly AnnotationSchema RelicSchema = new AnnotationSchema(
        "relic_id",
        "hook_source",
        new[] { "effect_tags", "effect_description", "resources_generated", "resources_consumed" });

    private static readonly AnnotationSchema PotionSchema = new AnnotationSchema(
        "potion_id",
        "on_use_source",
        new[] { "effect_tags", "effect_description" });

    private static readonly AnnotationSchema PowerSchema = new AnnotationSchema(
        "power_id",
        "hook_source",
        new[] { "effect_tags", "effect_description", "scaling_type" });

    private IReadOnlyDictionary<string, string> _cardKeywordGlossary;

    public MechanicalAnnotationRunner()
    {
        _cardKeywordGlossary = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public MechanicalAnnotationResult Run(
        CliOptions options,
        IReadOnlyList<string> cardIngestionPaths,
        IReadOnlyList<string> relicIngestionPaths,
        IReadOnlyList<string> potionIngestionPaths,
        string powerIngestionPath)
    {
        string model = string.IsNullOrWhiteSpace(options.ModelOverride)
            ? ModelRouter.Resolve(options.Provider, AnnotationTaskKind.Mechanical)
            : options.ModelOverride;

        ILlmProvider provider = LlmProviderFactory.Create(options, model);

        _cardKeywordGlossary = LoadCardKeywordGlossary(options.RootPath);

        Console.WriteLine($"Mechanical annotation: provider={options.Provider}, model={model}, batch-size={options.BatchSize}");

        // Gather all CSV files to process
        List<(string path, AnnotationSchema schema)> csvFiles = new List<(string, AnnotationSchema)>();
        for (int i = 0; i < cardIngestionPaths.Count; i++)
        {
            if (File.Exists(cardIngestionPaths[i]))
            {
                csvFiles.Add((cardIngestionPaths[i], CardSchema));
            }
        }

        for (int i = 0; i < relicIngestionPaths.Count; i++)
        {
            if (File.Exists(relicIngestionPaths[i]))
            {
                csvFiles.Add((relicIngestionPaths[i], RelicSchema));
            }
        }

        for (int i = 0; i < potionIngestionPaths.Count; i++)
        {
            if (File.Exists(potionIngestionPaths[i]))
            {
                csvFiles.Add((potionIngestionPaths[i], PotionSchema));
            }
        }

        if (!string.IsNullOrWhiteSpace(powerIngestionPath) && File.Exists(powerIngestionPath))
        {
            csvFiles.Add((powerIngestionPath, PowerSchema));
        }

        int annotated = 0;
        int skipped = 0;
        int batches = 0;

        if (provider is IBatchCapableLlmProvider batchProvider)
        {
            RunWithBatches(batchProvider, csvFiles, options.BatchSize, model, ref annotated, ref skipped, ref batches);
        }
        else
        {
            RunSequential(provider, model, csvFiles, options.BatchSize, ref annotated, ref skipped, ref batches);
        }

        return new MechanicalAnnotationResult
        {
            AnnotatedCount = annotated,
            SkippedCount = skipped,
            BatchCount = batches
        };
    }

    // ---- Batch path (Anthropic Message Batches API) ----

    private void RunWithBatches(
        IBatchCapableLlmProvider provider,
        List<(string path, AnnotationSchema schema)> csvFiles,
        int batchSize,
        string model,
        ref int annotated,
        ref int skipped,
        ref int batches)
    {
        // 1. Load all CSV data and build all batch requests
        List<PendingBatch> pendingBatches = new List<PendingBatch>();
        List<(string path, List<string> headers, List<Dictionary<string, string>> rows)> csvData =
            new List<(string, List<string>, List<Dictionary<string, string>>)>(csvFiles.Count);

        for (int fileIdx = 0; fileIdx < csvFiles.Count; fileIdx++)
        {
            (string csvPath, AnnotationSchema schema) = csvFiles[fileIdx];
            List<string> headers = ReadCsvHeaders(csvPath);
            List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
            csvData.Add((csvPath, headers, rows));

            if (rows.Count == 0)
            {
                continue;
            }

            List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> targets =
                CollectTargets(rows, schema, ref skipped);

            for (int start = 0; start < targets.Count; start += batchSize)
            {
                int end = Math.Min(start + batchSize, targets.Count);
                List<(int, string, string, string, string, string, string, string)> batchItems = targets.GetRange(start, end - start);

                string customId = $"f{fileIdx}_b{pendingBatches.Count}";
                string prompt = BuildBatchPrompt(batchItems, schema);
                AnnotationRequest request = new AnnotationRequest
                {
                    Model = model,
                    Prompt = prompt,
                    SystemPrompt = MechanicalSystemPrompt,
                    MaxTokens = BatchMaxTokens
                };

                pendingBatches.Add(new PendingBatch
                {
                    CustomId = customId,
                    FileIndex = fileIdx,
                    Schema = schema,
                    Items = batchItems.Select(static t => (t.Item1, t.Item2)).ToList(),
                    Request = request
                });
            }
        }

        if (pendingBatches.Count == 0)
        {
            return;
        }

        Console.WriteLine($"  Submitting {pendingBatches.Count} requests as a single Anthropic Message Batch...");

        // 2. Submit all as one message batch
        List<BatchAnnotationItem> batchItems2 = pendingBatches
            .Select(static pb => new BatchAnnotationItem { CustomId = pb.CustomId, Request = pb.Request })
            .ToList();

        IReadOnlyDictionary<string, BatchAnnotationResult> results =
            provider.BatchAnnotateAsync(batchItems2, CancellationToken.None).GetAwaiter().GetResult();

        batches += pendingBatches.Count;

        // 3. Apply results back to CSV rows
        for (int p = 0; p < pendingBatches.Count; p++)
        {
            PendingBatch pending = pendingBatches[p];
            if (!results.TryGetValue(pending.CustomId, out BatchAnnotationResult batchResult) || !batchResult.IsSuccess)
            {
                skipped += pending.Items.Count;
                continue;
            }

            List<Dictionary<string, string>> rows = csvData[pending.FileIndex].rows;
            Dictionary<string, Dictionary<string, string>> parsed = ParseAnnotationResponse(batchResult.Result.Content);
            ApplyParsedAnnotations(pending.Items, parsed, rows, pending.Schema, ref annotated, ref skipped);
        }

        // 4. Rewrite all CSV files
        for (int fileIdx = 0; fileIdx < csvData.Count; fileIdx++)
        {
            (string csvPath, List<string> headers, List<Dictionary<string, string>> rows) = csvData[fileIdx];
            RewriteEnrichedCsv(csvPath, headers, rows);
        }
    }

    // ---- Sequential path (non-batch providers) ----

    private void RunSequential(
        ILlmProvider provider,
        string model,
        List<(string path, AnnotationSchema schema)> csvFiles,
        int batchSize,
        ref int annotated,
        ref int skipped,
        ref int batches)
    {
        for (int i = 0; i < csvFiles.Count; i++)
        {
            AnnotateCsvFile(csvFiles[i].path, csvFiles[i].schema, provider, model, batchSize, ref annotated, ref skipped, ref batches);
        }
    }

    private void AnnotateCsvFile(
        string csvPath,
        AnnotationSchema schema,
        ILlmProvider provider,
        string model,
        int batchSize,
        ref int annotated,
        ref int skipped,
        ref int batches)
    {
        List<string> headers = ReadCsvHeaders(csvPath);
        List<Dictionary<string, string>> rows = CsvReader.ReadRows(csvPath);
        if (rows.Count == 0)
        {
            return;
        }

        List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> targets =
            CollectTargets(rows, schema, ref skipped);

        for (int start = 0; start < targets.Count; start += batchSize)
        {
            int end = Math.Min(start + batchSize, targets.Count);
            List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> batch =
                targets.GetRange(start, end - start);

            Console.WriteLine($"  [{schema.IdColumn}] Batch {batches + 1}: items {start + 1}-{end}/{targets.Count} ({Path.GetFileName(csvPath)})");

            Dictionary<string, Dictionary<string, string>> batchResults =
                CallLlmForBatch(batch, schema, provider, model);

            batches++;

            List<(int rowIndex, string id)> batchMapping = batch.Select(static t => (t.rowIndex, t.id)).ToList();
            ApplyParsedAnnotations(batchMapping, batchResults, rows, schema, ref annotated, ref skipped);
        }

        RewriteEnrichedCsv(csvPath, headers, rows);
    }

    private static List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> CollectTargets(
        List<Dictionary<string, string>> rows,
        AnnotationSchema schema,
        ref int skipped)
    {
        List<(int, string, string, string, string, string, string, string)> targets =
            new List<(int, string, string, string, string, string, string, string)>();

        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            string id = row.TryGetValue(schema.IdColumn, out string idVal) ? idVal : string.Empty;
            string title = row.TryGetValue("title", out string titleVal) ? titleVal : string.Empty;
            string source = row.TryGetValue(schema.SourceColumn, out string srcVal) ? srcVal : string.Empty;
            string keywords = row.TryGetValue("keywords", out string keywordVal) ? keywordVal : string.Empty;
            string energyCost = row.TryGetValue("energy_cost", out string energyVal) ? energyVal : string.Empty;
            string cardType = row.TryGetValue("type", out string cardTypeVal) ? cardTypeVal : string.Empty;
            string description = row.TryGetValue("description", out string descriptionVal) ? descriptionVal : string.Empty;

            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(source))
            {
                targets.Add((i, id, title, source, keywords, energyCost, cardType, description));
            }
            else
            {
                skipped++;
            }
        }

        return targets;
    }

    private static void ApplyParsedAnnotations(
        List<(int rowIndex, string id)> items,
        Dictionary<string, Dictionary<string, string>> parsed,
        List<Dictionary<string, string>> rows,
        AnnotationSchema schema,
        ref int annotated,
        ref int skipped)
    {
        for (int j = 0; j < items.Count; j++)
        {
            (int rowIndex, string id) = items[j];
            if (parsed.TryGetValue(id, out Dictionary<string, string> result))
            {
                for (int k = 0; k < schema.AnnotationColumns.Length; k++)
                {
                    string column = schema.AnnotationColumns[k];
                    if (result.TryGetValue(column, out string value))
                    {
                        if (string.Equals(column, "effect_tags", StringComparison.OrdinalIgnoreCase))
                        {
                            string existing = rows[rowIndex].TryGetValue(column, out string existingValue) ? existingValue : string.Empty;
                            rows[rowIndex][column] = MergeSemicolonValues(existing, value);
                        }
                        else
                        {
                            rows[rowIndex][column] = value;
                        }
                    }
                }

                annotated++;
            }
            else
            {
                skipped++;
            }
        }
    }

    private static string MergeSemicolonValues(string first, string second)
    {
        HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSemicolonValues(values, first);
        AddSemicolonValues(values, second);
        return string.Join(";", values.OrderBy(static v => v, StringComparer.Ordinal));
    }

    private static void AddSemicolonValues(HashSet<string> values, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string[] parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string value = parts[i].Trim();
            if (value.Length > 0)
            {
                values.Add(value);
            }
        }
    }

    private Dictionary<string, Dictionary<string, string>> CallLlmForBatch(
        List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> batch,
        AnnotationSchema schema,
        ILlmProvider provider,
        string model)
    {
        string prompt = BuildBatchPrompt(batch, schema);
        AnnotationRequest request = new AnnotationRequest
        {
            Model = model,
            Prompt = prompt,
            SystemPrompt = MechanicalSystemPrompt,
            MaxTokens = BatchMaxTokens
        };

        AnnotationResult result = provider.AnnotateAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        return ParseAnnotationResponse(result.Content);
    }

    private string BuildBatchPrompt(
        List<(int rowIndex, string id, string title, string source, string keywords, string energyCost, string cardType, string description)> batch,
        AnnotationSchema schema)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Annotate the following Slay the Spire 2 entities from their source code.");

        if (string.Equals(schema.IdColumn, CardSchema.IdColumn, StringComparison.Ordinal))
        {
            sb.AppendLine();
            sb.AppendLine("Card keyword glossary (authoritative):");
            foreach (KeyValuePair<string, string> item in _cardKeywordGlossary.OrderBy(static k => k.Key, StringComparer.Ordinal))
            {
                sb.AppendLine($"- {item.Key}: {item.Value}");
            }

            sb.AppendLine("When annotating cards, explicitly factor keyword implications into effect_tags, effect_description, resources_consumed, and anti_synergy_tags.");
            sb.AppendLine("Example: a Sly card with cost 2 is effectively free when paired with discard effects, so do not label it as expensive by default.");
        }

        sb.AppendLine("For each entity return a JSON object with these exact fields:");
        sb.AppendLine("  \"id\": entity id (unchanged)");
        sb.AppendLine("  \"effect_tags\": array of concise behavioral tags (e.g. deal_damage, gain_block, apply_vulnerable, draw_cards, gain_strength, apply_weak, apply_frail, gain_energy, exhaust_card, apply_buff, apply_debuff)");
        sb.AppendLine("  \"effect_description\": 1-2 sentence mechanical summary (mechanics only, no flavor)");

        if (schema.HasColumn("resources_generated"))
        {
            sb.AppendLine("  \"resources_generated\": array of resource production tags (e.g. generate_stars, gain_forge) or []");
        }

        if (schema.HasColumn("resources_consumed"))
        {
            sb.AppendLine("  \"resources_consumed\": array of resource consumption tags or []");
        }

        if (schema.HasColumn("scaling_type"))
        {
            sb.AppendLine("  \"scaling_type\": primary scaling driver — one of: strength, dexterity, focus, orbs, draw, block, none, other");
        }

        if (schema.HasColumn("anti_synergy_tags"))
        {
            sb.AppendLine("  \"anti_synergy_tags\": tags for mechanics this clashes with or []");
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON array. No markdown fencing. No extra text.");
        sb.AppendLine();

        bool isCardBatch = string.Equals(schema.IdColumn, CardSchema.IdColumn, StringComparison.Ordinal);
        List<object> entityList = new List<object>(batch.Count);
        for (int i = 0; i < batch.Count; i++)
        {
            (_, string id, string title, string source, string keywords, string energyCost, string cardType, string description) = batch[i];
            string truncated = source.Length > 2000 ? source.Substring(0, 2000) : source;
            if (isCardBatch)
            {
                entityList.Add(new
                {
                    id,
                    title,
                    keywords = ParseKeywords(keywords),
                    energy_cost = energyCost,
                    card_type = cardType,
                    description,
                    source = truncated
                });
            }
            else
            {
                entityList.Add(new { id, title, source = truncated });
            }
        }

        sb.Append(JsonSerializer.Serialize(entityList));
        return sb.ToString();
    }

    private static IReadOnlyList<string> ParseKeywords(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }

        string[] parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        List<string> values = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string keyword = parts[i].Trim();
            if (keyword.Length > 0)
            {
                values.Add(keyword);
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> LoadCardKeywordGlossary(string rootPath)
    {
        string[] candidates =
        {
            Path.Combine(rootPath, "sts2", "eng", "card_keywords.json"),
            Path.Combine(rootPath, "eng", "card_keywords.json")
        };

        string glossaryPath = candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        if (glossaryPath.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            string json = File.ReadAllText(glossaryPath);
            Dictionary<string, string> map = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            Dictionary<string, string> titlesByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> descriptionsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> entry in map)
            {
                if (entry.Key.EndsWith(".title", StringComparison.Ordinal))
                {
                    string token = entry.Key.Substring(0, entry.Key.Length - ".title".Length);
                    titlesByKey[token] = LocStringReader.StripFormatting(entry.Value);
                }
                else if (entry.Key.EndsWith(".description", StringComparison.Ordinal))
                {
                    string token = entry.Key.Substring(0, entry.Key.Length - ".description".Length);
                    descriptionsByKey[token] = LocStringReader.StripFormatting(entry.Value);
                }
            }

            Dictionary<string, string> glossary = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> titleEntry in titlesByKey)
            {
                if (descriptionsByKey.TryGetValue(titleEntry.Key, out string description) && !string.IsNullOrWhiteSpace(titleEntry.Value))
                {
                    glossary[titleEntry.Value] = description;
                }
            }

            return glossary;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[annotate] Failed to read card keyword glossary: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static Dictionary<string, Dictionary<string, string>> ParseAnnotationResponse(string content)
    {
        Dictionary<string, Dictionary<string, string>> results =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        string cleaned = content.Trim();

        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
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

            cleaned = cleaned.Trim();
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(cleaned);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (JsonElement element in root.EnumerateArray())
            {
                if (!element.TryGetProperty("id", out JsonElement idElement))
                {
                    continue;
                }

                string id = idElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                Dictionary<string, string> annotation =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                TryExtractStringField(element, "effect_description", annotation);
                TryExtractStringField(element, "scaling_type", annotation);
                TryExtractArrayField(element, "effect_tags", annotation);
                TryExtractArrayField(element, "resources_generated", annotation);
                TryExtractArrayField(element, "resources_consumed", annotation);
                TryExtractArrayField(element, "anti_synergy_tags", annotation);

                results[id] = annotation;
            }
        }
        catch (JsonException)
        {
            // Best-effort: return whatever was collected before the parse error
        }

        return results;
    }

    private static void TryExtractStringField(JsonElement element, string fieldName, Dictionary<string, string> target)
    {
        if (element.TryGetProperty(fieldName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String)
        {
            target[fieldName] = prop.GetString() ?? string.Empty;
        }
    }

    private static void TryExtractArrayField(JsonElement element, string fieldName, Dictionary<string, string> target)
    {
        if (!element.TryGetProperty(fieldName, out JsonElement prop))
        {
            return;
        }

        if (prop.ValueKind == JsonValueKind.Array)
        {
            List<string> items = new List<string>();
            foreach (JsonElement item in prop.EnumerateArray())
            {
                string value = item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : item.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    items.Add(value.Trim());
                }
            }

            target[fieldName] = string.Join(";", items);
        }
        else if (prop.ValueKind == JsonValueKind.String)
        {
            string raw = prop.GetString() ?? string.Empty;
            target[fieldName] = raw.Replace(", ", ";").Replace(",", ";");
        }
    }

    private static List<string> ReadCsvHeaders(string path)
    {
        using StreamReader reader = new StreamReader(path, Encoding.UTF8);
        string headerLine = reader.ReadLine() ?? string.Empty;
        List<string> headers = new List<string>();
        foreach (string part in headerLine.Split(','))
        {
            headers.Add(part.Trim('"').Trim());
        }

        return headers;
    }

    private static void RewriteEnrichedCsv(
        string path,
        List<string> columnOrder,
        List<Dictionary<string, string>> rows)
    {
        CsvWriter.Write(path, columnOrder, BuildRowValues(columnOrder, rows));
    }

    private static IEnumerable<IReadOnlyList<string>> BuildRowValues(
        List<string> columnOrder,
        List<Dictionary<string, string>> rows)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            Dictionary<string, string> row = rows[r];
            string[] values = new string[columnOrder.Count];
            for (int c = 0; c < columnOrder.Count; c++)
            {
                values[c] = row.TryGetValue(columnOrder[c], out string v) ? v : string.Empty;
            }

            yield return values;
        }
    }

    private sealed class AnnotationSchema
    {
        private readonly HashSet<string> _columnSet;

        public string IdColumn { get; }

        public string SourceColumn { get; }

        public string[] AnnotationColumns { get; }

        public AnnotationSchema(string idColumn, string sourceColumn, string[] annotationColumns)
        {
            IdColumn = idColumn;
            SourceColumn = sourceColumn;
            AnnotationColumns = annotationColumns;
            _columnSet = new HashSet<string>(annotationColumns, StringComparer.OrdinalIgnoreCase);
        }

        public bool HasColumn(string column)
        {
            return _columnSet.Contains(column);
        }
    }

    private sealed class PendingBatch
    {
        public string CustomId { get; set; }
        public int FileIndex { get; set; }
        public AnnotationSchema Schema { get; set; }
        public List<(int rowIndex, string id)> Items { get; set; }
        public AnnotationRequest Request { get; set; }

        public PendingBatch()
        {
            CustomId = string.Empty;
            Schema = null;
            Items = new List<(int, string)>();
            Request = new AnnotationRequest();
        }
    }
}

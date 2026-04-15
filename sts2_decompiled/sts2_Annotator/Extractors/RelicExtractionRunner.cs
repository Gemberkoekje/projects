using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class RelicExtractionRunner
{
    private readonly RelicExtractor _extractor;

    public RelicExtractionRunner()
    {
        _extractor = new RelicExtractor();
    }

    public RelicExtractionResult Run(CliOptions options)
    {
        string relicsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Relics");
        if (!Directory.Exists(relicsDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {relicsDirectory}");
        }

        IEnumerable<string> relicFiles = Directory
            .EnumerateFiles(relicsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.CharacterId))
        {
            HashSet<string> allowed = CharacterPoolReader.ReadRelicClassNames(options.RootPath, options.CharacterId);
            relicFiles = relicFiles.Where(f => allowed.Contains(Path.GetFileNameWithoutExtension(f)));
        }

        List<RelicExtractionRecord> records = relicFiles
            .Select(file => _extractor.Extract(options.RootPath, file))
            .ToList();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "relic_id",
            "id_resolution_status",
            "roslyn_fallback_used",
            "rarity",
            "amount_base",
            "counter_base",
            "hook_names",
            "confidence_score",
            "hook_source"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.RelicId,
            record.IdResolutionStatus,
            record.RoslynFallbackUsed ? "true" : "false",
            record.Rarity,
            IntOrEmpty(record.AmountBase),
            IntOrEmpty(record.CounterBase),
            string.Join(";", record.HookNames),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture),
            Compact(record.HookSource)
        }));

        string outputDirectory = Path.GetDirectoryName(options.OutputPath) ?? options.RootPath;
        LocStringReader locReader = LocStringReader.Create(options);

        List<RelicIngestionRecord> ingestionRecords = records.Select(record => new RelicIngestionRecord
        {
            RelicId = record.RelicId,
            CharacterId = options.CharacterId,
            Title = locReader.GetRelicTitle(record.RelicId),
            Description = locReader.GetRelicDescription(record.RelicId),
            FlavorText = locReader.GetRelicFlavor(record.RelicId),
            Rarity = record.Rarity,
            CounterBase = record.CounterBase,
            AmountBase = record.AmountBase,
            HookSource = record.HookSource,
            TriggersOn = record.HookNames.Select(MapHookToTrigger).Where(static x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList()
        }).ToList();

        string ingestionOutputPath = Path.Combine(outputDirectory, "relics_ingestion.csv");
        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "relic_id",
            "character_id",
            "title",
            "description",
            "flavor_text",
            "rarity",
            "counter_base",
            "amount_base",
            "hook_source",
            "effect_tags",
            "effect_description",
            "triggers_on",
            "trigger_condition",
            "resources_generated",
            "resources_consumed"
        }, ingestionRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.RelicId,
            record.CharacterId,
            record.Title,
            record.Description,
            record.FlavorText,
            record.Rarity,
            IntOrEmpty(record.CounterBase),
            IntOrEmpty(record.AmountBase),
            record.HookSource,
            string.Join(";", record.EffectTags),
            record.EffectDescription,
            string.Join(";", record.TriggersOn),
            record.TriggerCondition,
            string.Join(";", record.ResourcesGenerated),
            string.Join(";", record.ResourcesConsumed)
        }));

        List<HookListenerRecord> hookListeners = records
            .SelectMany(static record => record.HookNames.Select(hook => new HookListenerRecord
            {
                EntityType = "relic",
                EntityId = record.RelicId,
                HookName = hook
            }))
            .ToList();

        string hooksOutputPath = Path.Combine(outputDirectory, "relic_hook_listeners_staging.csv");
        CsvWriter.Write(hooksOutputPath, new[]
        {
            "entity_type",
            "entity_id",
            "hook_name"
        }, hookListeners.Select(static listener => (IReadOnlyList<string>)new[]
        {
            listener.EntityType,
            listener.EntityId,
            listener.HookName
        }));

        return new RelicExtractionResult
        {
            OutputPath = options.OutputPath,
            IngestionOutputPath = ingestionOutputPath,
            HooksOutputPath = hooksOutputPath,
            RecordCount = records.Count,
            HookCount = hookListeners.Count,
            LowConfidenceCount = records.Count(r => r.ConfidenceScore < options.ConfidenceThreshold)
        };
    }

    private static string MapHookToTrigger(string hookName)
    {
        if (string.IsNullOrWhiteSpace(hookName))
        {
            return string.Empty;
        }

        return hookName;
    }

    private static string IntOrEmpty(int value)
    {
        return value == int.MinValue ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Compact(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return source.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class PowerExtractionRunner
{
    private readonly PowerExtractor _extractor;

    public PowerExtractionRunner()
    {
        _extractor = new PowerExtractor();
    }

    public PowerExtractionResult Run(CliOptions options)
    {
        string powersDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Powers");
        if (!Directory.Exists(powersDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {powersDirectory}");
        }

        List<PowerExtractionRecord> records = Directory
            .EnumerateFiles(powersDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _extractor.Extract(options.RootPath, file))
            .ToList();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "power_id",
            "id_resolution_status",
            "roslyn_fallback_used",
            "power_type",
            "stack_type",
            "is_player_power",
            "hook_names",
            "confidence_score",
            "hook_source"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.PowerId,
            record.IdResolutionStatus,
            record.RoslynFallbackUsed ? "true" : "false",
            record.PowerType,
            record.StackType,
            record.IsPlayerPower ? "true" : "false",
            string.Join(";", record.HookNames),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture),
            Compact(record.HookSource)
        }));

        string outputDirectory = Path.GetDirectoryName(options.OutputPath) ?? options.RootPath;
        string ingestionOutputPath = Path.Combine(outputDirectory, "powers_ingestion.csv");
        LocStringReader locReader = LocStringReader.Create(options);

        List<PowerIngestionRecord> ingestionRecords = records.Select(record => new PowerIngestionRecord
        {
            PowerId = record.PowerId,
            Title = locReader.GetPowerTitle(record.PowerId),
            Description = locReader.GetPowerDescription(record.PowerId),
            Type = record.PowerType,
            StackType = record.StackType,
            IsPlayerPower = record.IsPlayerPower,
            HookSource = record.HookSource,
            TriggersOn = record.HookNames.Select(MapHookToTrigger).Where(static x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList()
        }).ToList();

        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "power_id",
            "title",
            "description",
            "type",
            "stack_type",
            "is_player_power",
            "hook_source",
            "effect_tags",
            "effect_description",
            "triggers_on",
            "scaling_type"
        }, ingestionRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.PowerId,
            record.Title,
            record.Description,
            record.Type,
            record.StackType,
            record.IsPlayerPower ? "true" : "false",
            record.HookSource,
            string.Join(";", record.EffectTags),
            record.EffectDescription,
            string.Join(";", record.TriggersOn),
            record.ScalingType
        }));

        List<HookListenerRecord> hookListeners = records
            .SelectMany(static record => record.HookNames.Select(hook => new HookListenerRecord
            {
                EntityType = "power",
                EntityId = record.PowerId,
                HookName = hook
            }))
            .ToList();

        string hooksOutputPath = Path.Combine(outputDirectory, "power_hook_listeners_staging.csv");
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

        return new PowerExtractionResult
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

    private static string Compact(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return source.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

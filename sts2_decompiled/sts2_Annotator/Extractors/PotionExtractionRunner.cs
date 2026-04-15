using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class PotionExtractionRunner
{
    private readonly PotionExtractor _extractor;

    public PotionExtractionRunner()
    {
        _extractor = new PotionExtractor();
    }

    public PotionExtractionResult Run(CliOptions options)
    {
        string potionsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Potions");
        if (!Directory.Exists(potionsDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {potionsDirectory}");
        }

        IEnumerable<string> potionFiles = Directory
            .EnumerateFiles(potionsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.CharacterId))
        {
            HashSet<string> allowed = CharacterPoolReader.ReadPotionClassNames(options.RootPath, options.CharacterId);
            potionFiles = potionFiles.Where(f => allowed.Contains(Path.GetFileNameWithoutExtension(f)));
        }

        List<PotionExtractionRecord> records = potionFiles
            .Select(file => _extractor.Extract(options.RootPath, file))
            .ToList();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "potion_id",
            "id_resolution_status",
            "roslyn_fallback_used",
            "rarity",
            "target_type",
            "amount_base",
            "confidence_score",
            "on_use_source"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.PotionId,
            record.IdResolutionStatus,
            record.RoslynFallbackUsed ? "true" : "false",
            record.Rarity,
            record.TargetType,
            IntOrEmpty(record.AmountBase),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture),
            Compact(record.OnUseSource)
        }));

        string ingestionOutputPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? options.RootPath, "potions_ingestion.csv");
        LocStringReader locReader = LocStringReader.Create(options);
        List<PotionIngestionRecord> ingestionRecords = records.Select(record => new PotionIngestionRecord
        {
            PotionId = record.PotionId,
            CharacterId = options.CharacterId,
            Title = locReader.GetPotionTitle(record.PotionId),
            Description = locReader.GetPotionDescription(record.PotionId),
            Rarity = record.Rarity,
            AmountBase = record.AmountBase,
            OnUseSource = record.OnUseSource
        }).ToList();

        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "potion_id",
            "character_id",
            "title",
            "description",
            "rarity",
            "amount_base",
            "on_use_source",
            "effect_tags",
            "effect_description"
        }, ingestionRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.PotionId,
            record.CharacterId,
            record.Title,
            record.Description,
            record.Rarity,
            IntOrEmpty(record.AmountBase),
            record.OnUseSource,
            string.Join(";", record.EffectTags),
            record.EffectDescription
        }));

        return new PotionExtractionResult
        {
            OutputPath = options.OutputPath,
            IngestionOutputPath = ingestionOutputPath,
            RecordCount = records.Count,
            LowConfidenceCount = records.Count(r => r.ConfidenceScore < options.ConfidenceThreshold)
        };
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

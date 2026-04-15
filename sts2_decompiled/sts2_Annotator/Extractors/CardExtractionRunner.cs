using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class CardExtractionRunner
{
    private readonly CardExtractor _extractor;

    public CardExtractionRunner()
    {
        _extractor = new CardExtractor();
    }

    public CardExtractionResult Run(CliOptions options)
    {
        string cardsDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Cards");
        if (!Directory.Exists(cardsDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {cardsDirectory}");
        }

        IEnumerable<string> cardFiles = Directory
            .EnumerateFiles(cardsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(options.CharacterId))
        {
            HashSet<string> allowed = CharacterPoolReader.ReadCardClassNames(options.RootPath, options.CharacterId);
            cardFiles = cardFiles.Where(f => allowed.Contains(Path.GetFileNameWithoutExtension(f)));
        }

        List<CardExtractionRecord> records = cardFiles
            .Select(file => _extractor.Extract(options.RootPath, file))
            .ToList();

        // Output 1: Extraction audit (raw parsed data with confidence scores)
        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "card_id",
            "id_resolution_status",
            "roslyn_fallback_used",
            "energy_cost",
            "type",
            "rarity",
            "target",
            "gains_block",
            "keywords",
            "max_upgrade",
            "damage_base",
            "damage_upgraded",
            "block_base",
            "block_upgraded",
            "magic_base",
            "magic_upgraded",
            "confidence_score",
            "effect_tags",
            "companion_actions",
            "on_play_source",
            "on_upgrade_source"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.CardId,
            record.IdResolutionStatus,
            record.RoslynFallbackUsed ? "true" : "false",
            IntOrEmpty(record.EnergyCost),
            record.CardType,
            record.CardRarity,
            record.TargetType,
            record.GainsBlock ? "true" : "false",
            string.Join(";", record.Keywords),
            record.MaxUpgradeLevel.ToString(CultureInfo.InvariantCulture),
            IntOrEmpty(record.DamageBase),
            IntOrEmpty(record.DamageUpgraded),
            IntOrEmpty(record.BlockBase),
            IntOrEmpty(record.BlockUpgraded),
            IntOrEmpty(record.MagicBase),
            IntOrEmpty(record.MagicUpgraded),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture),
            string.Join(";", record.EffectTags),
            string.Join(";", record.CompanionActions.Select(static action => $"{action.CompanionId}|{action.ActionTag}|{action.SourceMethod}")),
            Compact(record.OnPlaySource),
            Compact(record.OnUpgradeSource)
        }));

        // Output 2: Companion actions staging table
        string companionOutputPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? options.RootPath, "card_companion_actions_staging.csv");
        List<CompanionActionRecord> companionActions = records
            .SelectMany(static r => r.CompanionActions)
            .ToList();

        CsvWriter.Write(companionOutputPath, new[]
        {
            "card_id",
            "companion_id",
            "action_tag",
            "source_method"
        }, companionActions.Select(static action => (IReadOnlyList<string>)new[]
        {
            action.CardId,
            action.CompanionId,
            action.ActionTag,
            action.SourceMethod
        }));

        // Output 3: Convert to ingestion format (base/upgraded only)
        LocStringReader locReader = LocStringReader.Create(options);
        CardIngestionConverter converter = new CardIngestionConverter(locReader);
        List<CardIngestionRecord> ingestionRecords = records
            .Select(r => converter.ToIngestionRecord(r, options.CharacterId))
            .ToList();

        string ingestionOutputPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? options.RootPath, "cards_ingestion.csv");
        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "card_id",
            "character_id",
            "title",
            "description",
            "type",
            "rarity",
            "energy_cost",
            "target",
            "gains_block",
            "keywords",
            "damage_base",
            "damage_upgraded",
            "block_base",
            "block_upgraded",
            "magic_base",
            "magic_upgraded",
            "max_upgrade",
            "on_play_source",
            "on_upgrade_source",
            "effect_tags",
            "effect_description",
            "resources_generated",
            "resources_consumed",
            "scaling_type",
            "anti_synergy_tags"
        }, ingestionRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.CardId,
            record.CharacterId,
            record.Title,
            record.Description,
            record.Type,
            record.Rarity,
            record.EnergyCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.Target,
            record.GainsBlock ? "true" : "false",
            string.Join(";", record.Keywords),
            record.DamageBase?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.DamageUpgraded?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.BlockBase?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.BlockUpgraded?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.MagicBase?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.MagicUpgraded?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.MaxUpgrade.ToString(CultureInfo.InvariantCulture),
            record.OnPlaySource,
            record.OnUpgradeSource,
            string.Join(";", record.EffectTags),
            record.EffectDescription,
            string.Join(";", record.ResourcesGenerated),
            string.Join(";", record.ResourcesConsumed),
            record.ScalingType,
            string.Join(";", record.AntiSynergyTags)
        }));

        // Output 4: Card variants (exceptions - currently mostly empty for STS2)
        List<CardVariantRecord> variantRecords = new List<CardVariantRecord>();
        foreach (var record in ingestionRecords)
        {
            variantRecords.AddRange(converter.ExtractVariants(record.CardId, record.MaxUpgrade));
        }

        string variantOutputPath = Path.Combine(Path.GetDirectoryName(options.OutputPath) ?? options.RootPath, "card_variants_staging.csv");
        CsvWriter.Write(variantOutputPath, new[]
        {
            "card_id",
            "variant_kind",
            "upgrade_level",
            "source_id",
            "energy_cost",
            "damage_value",
            "block_value",
            "magic_value",
            "effect_tags",
            "effect_description",
            "resources_generated",
            "resources_consumed",
            "scaling_type",
            "anti_synergy_tags"
        }, variantRecords.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.CardId,
            record.VariantKind,
            record.UpgradeLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.SourceId,
            record.EnergyCost?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.DamageValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.BlockValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            record.MagicValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            string.Join(";", record.EffectTags),
            record.EffectDescription,
            string.Join(";", record.ResourcesGenerated),
            string.Join(";", record.ResourcesConsumed),
            record.ScalingType,
            string.Join(";", record.AntiSynergyTags)
        }));

        return new CardExtractionResult
        {
            OutputPath = options.OutputPath,
            CompanionOutputPath = companionOutputPath,
            RecordCount = records.Count,
            LowConfidenceCount = records.Count(r => r.ConfidenceScore < options.ConfidenceThreshold),
            CompanionActionCount = companionActions.Count,
            IngestionOutputPath = ingestionOutputPath,
            VariantOutputPath = variantOutputPath,
            VariantCount = variantRecords.Count
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sts2Extractor.Cli;
using Sts2Extractor.Infrastructure;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

internal sealed class CharacterExtractionRunner
{
    private static readonly HashSet<string> KnownCharacterIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ironclad",
        "silent",
        "defect",
        "regent",
        "necrobinder"
    };

    private readonly CharacterExtractor _extractor;

    public CharacterExtractionRunner()
    {
        _extractor = new CharacterExtractor();
    }

    public CharacterExtractionResult Run(CliOptions options)
    {
        string charactersDirectory = Path.Combine(options.RootPath, "MegaCrit.Sts2.Core.Models.Characters");
        if (!Directory.Exists(charactersDirectory))
        {
            throw new DirectoryNotFoundException($"Missing expected directory: {charactersDirectory}");
        }

        List<CharacterExtractionRecord> records = Directory
            .EnumerateFiles(charactersDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => _extractor.Extract(options.RootPath, file))
            .Where(r => r.CharacterId.Length > 0 && KnownCharacterIds.Contains(r.CharacterId))
            .ToList();

        CsvWriter.Write(options.OutputPath, new[]
        {
            "file",
            "character_id",
            "starting_hp",
            "starting_gold",
            "max_energy",
            "orb_slots",
            "starting_deck",
            "starting_relics",
            "confidence_score"
        }, records.Select(static record => (IReadOnlyList<string>)new[]
        {
            record.FilePath,
            record.CharacterId,
            IntOrEmpty(record.StartingHp),
            IntOrEmpty(record.StartingGold),
            record.MaxEnergy.ToString(CultureInfo.InvariantCulture),
            record.OrbSlots.ToString(CultureInfo.InvariantCulture),
            string.Join(";", record.StartingDeck),
            string.Join(";", record.StartingRelics),
            record.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture)
        }));

        string outputDirectory = Path.GetDirectoryName(options.OutputPath) ?? options.RootPath;
        LocStringReader locReader = LocStringReader.Create(options);

        string ingestionOutputPath = Path.Combine(outputDirectory, "characters_ingestion.csv");
        CsvWriter.Write(ingestionOutputPath, new[]
        {
            "character_id",
            "title",
            "description",
            "starting_hp",
            "starting_gold",
            "max_energy",
            "orb_slots",
            "starting_deck",
            "starting_relics"
        }, records.Select(record => (IReadOnlyList<string>)new[]
        {
            record.CharacterId,
            locReader.GetCharacterTitle(record.CharacterId),
            locReader.GetCharacterDescription(record.CharacterId),
            IntOrEmpty(record.StartingHp),
            IntOrEmpty(record.StartingGold),
            record.MaxEnergy.ToString(CultureInfo.InvariantCulture),
            record.OrbSlots.ToString(CultureInfo.InvariantCulture),
            string.Join(";", record.StartingDeck),
            string.Join(";", record.StartingRelics)
        }));

        return new CharacterExtractionResult
        {
            OutputPath = options.OutputPath,
            IngestionOutputPath = ingestionOutputPath,
            RecordCount = records.Count
        };
    }

    private static string IntOrEmpty(int value)
        => value == int.MinValue ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}

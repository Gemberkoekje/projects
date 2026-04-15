using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class CharacterExtractor
{
    private static readonly Regex StartingHpRegex = new Regex(@"override\s+int\s+StartingHp\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex StartingGoldRegex = new Regex(@"override\s+int\s+StartingGold\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex MaxEnergyRegex = new Regex(@"override\s+int\s+MaxEnergy\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex OrbSlotRegex = new Regex(@"override\s+int\s+BaseOrbSlotCount\s*=>\s*(?<value>\d+)", RegexOptions.Compiled);
    private static readonly Regex DeckCardRegex = new Regex(@"ModelDb\.Card<(\w+)>\(\)", RegexOptions.Compiled);
    private static readonly Regex StartingRelicRegex = new Regex(@"ModelDb\.Relic<(\w+)>\(\)", RegexOptions.Compiled);

    public CharacterExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = File.ReadAllText(filePath);
        CharacterExtractionRecord record = new CharacterExtractionRecord
        {
            FilePath = Path.GetRelativePath(rootPath, filePath)
        };

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType);
        if (!hasClassInfo || !SourcePatterns.IsTypeName(baseType, "CharacterModel"))
        {
            return record;
        }

        record.CharacterId = className.ToLowerInvariant();

        Match hpMatch = StartingHpRegex.Match(source);
        if (hpMatch.Success && int.TryParse(hpMatch.Groups["value"].Value, out int hp))
        {
            record.StartingHp = hp;
        }

        Match goldMatch = StartingGoldRegex.Match(source);
        if (goldMatch.Success && int.TryParse(goldMatch.Groups["value"].Value, out int gold))
        {
            record.StartingGold = gold;
        }

        Match energyMatch = MaxEnergyRegex.Match(source);
        if (energyMatch.Success && int.TryParse(energyMatch.Groups["value"].Value, out int energy))
        {
            record.MaxEnergy = energy;
        }

        Match orbMatch = OrbSlotRegex.Match(source);
        if (orbMatch.Success && int.TryParse(orbMatch.Groups["value"].Value, out int orbSlots))
        {
            record.OrbSlots = orbSlots;
        }

        foreach (Match m in DeckCardRegex.Matches(source))
        {
            record.StartingDeck.Add(m.Groups[1].Value);
        }

        foreach (Match m in StartingRelicRegex.Matches(source))
        {
            record.StartingRelics.Add(m.Groups[1].Value);
        }

        int signals = 0;
        if (record.CharacterId.Length > 0) signals++;
        if (record.StartingHp != int.MinValue) signals++;
        if (record.StartingGold != int.MinValue) signals++;
        if (record.StartingDeck.Count > 0) signals++;
        record.ConfidenceScore = signals / 4m;

        return record;
    }
}

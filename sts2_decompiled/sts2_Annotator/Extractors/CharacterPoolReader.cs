using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sts2Extractor.Extractors;

internal static class CharacterPoolReader
{
    private static readonly Regex CardPattern = new Regex(@"ModelDb\.Card<(\w+)>\(\)", RegexOptions.Compiled);
    private static readonly Regex RelicPattern = new Regex(@"ModelDb\.Relic<(\w+)>\(\)", RegexOptions.Compiled);
    private static readonly Regex PotionPattern = new Regex(@"ModelDb\.Potion<(\w+)>\(\)", RegexOptions.Compiled);

    public static HashSet<string> ReadCardClassNames(string root, string characterId)
        => ReadClassNames(root, "MegaCrit.Sts2.Core.Models.CardPools", ToPascalCase(characterId) + "CardPool.cs", CardPattern);

    public static HashSet<string> ReadRelicClassNames(string root, string characterId)
        => ReadClassNames(root, "MegaCrit.Sts2.Core.Models.RelicPools", ToPascalCase(characterId) + "RelicPool.cs", RelicPattern);

    public static HashSet<string> ReadPotionClassNames(string root, string characterId)
        => ReadClassNames(root, "MegaCrit.Sts2.Core.Models.PotionPools", ToPascalCase(characterId) + "PotionPool.cs", PotionPattern);

    private static HashSet<string> ReadClassNames(string root, string subdir, string fileName, Regex pattern)
    {
        string filePath = Path.Combine(root, subdir, fileName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Character pool file not found: {filePath}");
        }

        string source = File.ReadAllText(filePath);
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in pattern.Matches(source))
        {
            names.Add(match.Groups[1].Value);
        }

        return names;
    }

    private static string ToPascalCase(string characterId)
    {
        if (string.IsNullOrEmpty(characterId))
        {
            return characterId;
        }

        return char.ToUpperInvariant(characterId[0]) + characterId.Substring(1);
    }
}

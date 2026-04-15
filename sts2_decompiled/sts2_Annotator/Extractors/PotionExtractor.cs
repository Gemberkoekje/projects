using System;
using System.Text.RegularExpressions;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class PotionExtractor
{
    public PotionExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = System.IO.File.ReadAllText(filePath);
        PotionExtractionRecord potion = new PotionExtractionRecord
        {
            FilePath = System.IO.Path.GetRelativePath(rootPath, filePath)
        };

        int confidenceParts = 0;
        bool roslynFallbackUsed = false;

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType, out bool classFallbackUsed);
        roslynFallbackUsed |= classFallbackUsed;
        if (hasClassInfo && SourcePatterns.IsTypeName(baseType, "PotionModel"))
        {
            potion.PotionId = className;
            potion.IdResolutionStatus = "class_name";
            confidenceParts++;
        }

        if (potion.PotionId.Length == 0)
        {
            potion.PotionId = System.IO.Path.GetFileNameWithoutExtension(filePath);
            potion.IdResolutionStatus = "filename_fallback";
        }

        potion.Rarity = ReadEnumValue(source, "PotionRarity");
        if (potion.Rarity.Length > 0)
        {
            confidenceParts++;
        }

        potion.TargetType = ReadEnumValue(source, "TargetType");
        potion.AmountBase = SourcePatterns.ReadIntLiteral(source, @"new\s+\w+Var\((?<value>-?\d+(?:\.\d+)?m?)");

        potion.OnUseSource = SourcePatterns.ExtractMethodBlock(source, "OnUse", out bool onUseFallbackUsed);
        roslynFallbackUsed |= onUseFallbackUsed;
        if (potion.OnUseSource.Length > 0)
        {
            confidenceParts++;
        }

        if (potion.TargetType.Length > 0 || potion.AmountBase != int.MinValue)
        {
            confidenceParts++;
        }

        potion.RoslynFallbackUsed = roslynFallbackUsed;
        potion.ConfidenceScore = confidenceParts / 4m;
        return potion;
    }

    private static string ReadEnumValue(string source, string enumName)
    {
        Match match = Regex.Match(source, @"\b(?:" + enumName + @"\s+)?\w+\s*=>\s*" + enumName + @"\.(?<value>\w+)", RegexOptions.Singleline);
        if (!match.Success)
        {
            return string.Empty;
        }

        return match.Groups["value"].Value.Trim();
    }
}

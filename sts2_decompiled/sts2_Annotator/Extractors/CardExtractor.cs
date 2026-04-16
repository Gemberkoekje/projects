using System;
using System.Collections.Generic;
using System.Linq;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class CardExtractor
{
    public CardExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = System.IO.File.ReadAllText(filePath);
        CardExtractionRecord card = new CardExtractionRecord
        {
            FilePath = System.IO.Path.GetRelativePath(rootPath, filePath)
        };

        int confidenceParts = 0;
        bool roslynFallbackUsed = false;

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType, out bool classFallbackUsed);
        roslynFallbackUsed |= classFallbackUsed;
        if (hasClassInfo && SourcePatterns.IsTypeName(baseType, "CardModel"))
        {
            card.CardId = className;
            card.IdResolutionStatus = "class_name";
            confidenceParts++;
        }

        if (card.CardId.Length == 0)
        {
            card.CardId = System.IO.Path.GetFileNameWithoutExtension(filePath);
            card.IdResolutionStatus = "filename_fallback";
        }

        if (SourcePatterns.TryGetBaseConstructorArgs(source, out string[] args, out bool ctorFallbackUsed) && args.Length >= 4)
        {
            roslynFallbackUsed |= ctorFallbackUsed;
            card.EnergyCost = ParseCtorInt(args[0]);
            card.CardType = StripEnumPrefix(args[1], "CardType.");
            card.CardRarity = StripEnumPrefix(args[2], "CardRarity.");
            card.TargetType = StripEnumPrefix(args[3], "TargetType.");
            confidenceParts++;
        }

        card.GainsBlock = source.Contains("GainsBlock => true", StringComparison.Ordinal);
        card.Keywords = SourcePatterns.ReadCardKeywords(source);
        int maxUpgrade = SourcePatterns.ReadIntLiteral(source, @"MaxUpgradeLevel\s*=>\s*(?<value>\d+)");
        card.MaxUpgradeLevel = maxUpgrade == int.MinValue ? 1 : maxUpgrade;

        int damageBase = SourcePatterns.ReadIntLiteral(source, @"new\s+DamageVar\((?<value>-?\d+(?:\.\d+)?m?)");
        int damageUpgrade = SourcePatterns.ReadIntLiteral(source, @"DynamicVars\.Damage\.UpgradeValueBy\((?<value>-?\d+(?:\.\d+)?m?)");
        card.DamageBase = damageBase;
        card.DamageUpgraded = CombineUpgrade(damageBase, damageUpgrade);

        int blockBase = SourcePatterns.ReadIntLiteral(source, @"new\s+BlockVar\((?<value>-?\d+(?:\.\d+)?m?)");
        int blockUpgrade = SourcePatterns.ReadIntLiteral(source, @"DynamicVars\.Block\.UpgradeValueBy\((?<value>-?\d+(?:\.\d+)?m?)");
        card.BlockBase = blockBase;
        card.BlockUpgraded = CombineUpgrade(blockBase, blockUpgrade);

        int magicBase = SourcePatterns.ReadIntLiteral(source, @"new\s+(PowerVar<[^>]+>|DynamicVar)\([^\)]*?(?<value>-?\d+(?:\.\d+)?m?)\)");
        int magicUpgrade = SourcePatterns.ReadIntLiteral(source, @"DynamicVars\.[A-Za-z]+\.UpgradeValueBy\((?<value>-?\d+(?:\.\d+)?m?)");
        card.MagicBase = magicBase;
        card.MagicUpgraded = CombineUpgrade(magicBase, magicUpgrade);

        card.OnPlaySource = SourcePatterns.ExtractMethodBlock(source, "OnPlay", out bool onPlayFallbackUsed);
        roslynFallbackUsed |= onPlayFallbackUsed;
        if (card.OnPlaySource.Length > 0)
        {
            confidenceParts++;
        }

        card.OnUpgradeSource = SourcePatterns.ExtractMethodBlock(source, "OnUpgrade", out bool onUpgradeFallbackUsed);
        roslynFallbackUsed |= onUpgradeFallbackUsed;
        if (card.OnUpgradeSource.Length > 0)
        {
            confidenceParts++;
        }

        card.CompanionActions = DetectCompanionActions(card.CardId, source, card.OnPlaySource, card.OnUpgradeSource);
        card.EffectTags = BuildEffectTags(card.Keywords, card.CompanionActions);
        card.RoslynFallbackUsed = roslynFallbackUsed;
        card.ConfidenceScore = confidenceParts / 4m;
        return card;
    }

    private static IReadOnlyList<CompanionActionRecord> DetectCompanionActions(string cardId, string source, string onPlaySource, string onUpgradeSource)
    {
        List<CompanionActionRecord> records = new List<CompanionActionRecord>();
        bool hasFromOsty = source.Contains("FromOsty(", StringComparison.Ordinal);
        bool hasOstyCmd = source.Contains("OstyCmd.", StringComparison.Ordinal);
        if (!hasFromOsty && !hasOstyCmd)
        {
            return records;
        }

        if (hasFromOsty)
        {
            AddCompanionAction(records, cardId, "Osty", "osty_damage_source", ResolveSourceMethod("FromOsty(", onPlaySource, onUpgradeSource));
        }

        if (hasOstyCmd)
        {
            AddCompanionAction(records, cardId, "Osty", "osty_attack", ResolveSourceMethod("OstyCmd.", onPlaySource, onUpgradeSource));
        }

        return records;
    }

    private static IReadOnlyList<string> BuildEffectTags(IReadOnlyList<string> keywords, IReadOnlyList<CompanionActionRecord> companionActions)
    {
        HashSet<string> tags = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < keywords.Count; i++)
        {
            string mapped = MapKeywordToEffectTag(keywords[i]);
            if (mapped.Length > 0)
            {
                tags.Add(mapped);
            }
        }

        if (companionActions.Count > 0)
        {
            tags.Add("companion_synergy");
            foreach (CompanionActionRecord action in companionActions)
            {
                tags.Add(action.ActionTag);
            }
        }

        return tags.ToList();
    }

    private static string MapKeywordToEffectTag(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return string.Empty;
        }

        return keyword.Trim().ToLowerInvariant() switch
        {
            "none" => string.Empty,
            "sly" => "sly",
            "retain" => "retain",
            "innate" => "innate",
            "exhaust" => "exhaust",
            "ethereal" => "ethereal",
            "unplayable" => "unplayable",
            "eternal" => "eternal",
            _ => string.Empty
        };
    }

    private static string ResolveSourceMethod(string marker, string onPlaySource, string onUpgradeSource)
    {
        if (onPlaySource.Contains(marker, StringComparison.Ordinal))
        {
            return "OnPlay";
        }

        if (onUpgradeSource.Contains(marker, StringComparison.Ordinal))
        {
            return "OnUpgrade";
        }

        return "Unknown";
    }

    private static void AddCompanionAction(List<CompanionActionRecord> records, string cardId, string companionId, string actionTag, string sourceMethod)
    {
        bool exists = records.Any(r =>
            string.Equals(r.CardId, cardId, StringComparison.Ordinal) &&
            string.Equals(r.CompanionId, companionId, StringComparison.Ordinal) &&
            string.Equals(r.ActionTag, actionTag, StringComparison.Ordinal) &&
            string.Equals(r.SourceMethod, sourceMethod, StringComparison.Ordinal));

        if (exists)
        {
            return;
        }

        records.Add(new CompanionActionRecord
        {
            CardId = cardId,
            CompanionId = companionId,
            ActionTag = actionTag,
            SourceMethod = sourceMethod
        });
    }

    private static int ParseCtorInt(string value)
    {
        string raw = value.Trim();
        if (raw.StartsWith("new ", StringComparison.Ordinal))
        {
            return int.MinValue;
        }

        if (int.TryParse(raw, out int parsed))
        {
            return parsed;
        }

        return int.MinValue;
    }

    private static string StripEnumPrefix(string value, string prefix)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return trimmed.Substring(prefix.Length);
        }

        return trimmed;
    }

    private static int CombineUpgrade(int baseValue, int delta)
    {
        if (baseValue == int.MinValue)
        {
            return int.MinValue;
        }

        if (delta == int.MinValue)
        {
            return baseValue;
        }

        return baseValue + delta;
    }
}

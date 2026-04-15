using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class RelicExtractor
{
    public RelicExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = System.IO.File.ReadAllText(filePath);
        RelicExtractionRecord relic = new RelicExtractionRecord
        {
            FilePath = System.IO.Path.GetRelativePath(rootPath, filePath)
        };

        int confidenceParts = 0;
        bool roslynFallbackUsed = false;

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType, out bool classFallbackUsed);
        roslynFallbackUsed |= classFallbackUsed;
        if (hasClassInfo && SourcePatterns.IsTypeName(baseType, "RelicModel"))
        {
            relic.RelicId = className;
            relic.IdResolutionStatus = "class_name";
            confidenceParts++;
        }

        if (relic.RelicId.Length == 0)
        {
            relic.RelicId = System.IO.Path.GetFileNameWithoutExtension(filePath);
            relic.IdResolutionStatus = "filename_fallback";
        }

        relic.Rarity = ReadEnumValue(source, "RelicRarity");
        if (relic.Rarity.Length > 0)
        {
            confidenceParts++;
        }

        relic.AmountBase = SourcePatterns.ReadIntLiteral(source, @"new\s+\w+Var\((?<value>-?\d+(?:\.\d+)?m?)");
        relic.CounterBase = SourcePatterns.ReadIntLiteral(source, @"Counter\s*=>\s*(?<value>-?\d+(?:\.\d+)?m?)");

        IReadOnlyList<string> hooks = SourcePatterns.GetHookMethodNames(source);
        relic.HookNames = hooks;
        relic.HookSource = JoinHookSources(source, hooks, ref roslynFallbackUsed);
        if (hooks.Count > 0)
        {
            confidenceParts++;
        }

        if (relic.AmountBase != int.MinValue || relic.CounterBase != int.MinValue)
        {
            confidenceParts++;
        }

        relic.RoslynFallbackUsed = roslynFallbackUsed;
        relic.ConfidenceScore = confidenceParts / 4m;
        return relic;
    }

    private static string JoinHookSources(string source, IReadOnlyList<string> hooks, ref bool roslynFallbackUsed)
    {
        List<string> blocks = new List<string>();
        foreach (string hook in hooks)
        {
            string body = SourcePatterns.ExtractMethodBlock(source, hook, out bool fallbackUsed);
            roslynFallbackUsed |= fallbackUsed;
            if (body.Length == 0)
            {
                continue;
            }

            blocks.Add(hook + " " + body);
        }

        return string.Join("\n\n", blocks);
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

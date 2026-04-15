using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Extractors;

internal sealed class PowerExtractor
{
    public PowerExtractionRecord Extract(string rootPath, string filePath)
    {
        string source = System.IO.File.ReadAllText(filePath);
        PowerExtractionRecord power = new PowerExtractionRecord
        {
            FilePath = System.IO.Path.GetRelativePath(rootPath, filePath)
        };

        int coreSignals = 0;
        bool roslynFallbackUsed = false;

        bool hasClassInfo = SourcePatterns.TryGetClassInfo(source, out string className, out string baseType, out bool classFallbackUsed);
        roslynFallbackUsed |= classFallbackUsed;
        if (hasClassInfo && SourcePatterns.IsTypeName(baseType, "PowerModel"))
        {
            power.PowerId = className;
            power.IdResolutionStatus = "class_name";
            coreSignals++;
        }

        if (power.PowerId.Length == 0)
        {
            power.PowerId = System.IO.Path.GetFileNameWithoutExtension(filePath);
            power.IdResolutionStatus = "filename_fallback";
        }

        power.PowerType = ReadEnumValue(source, "PowerType");
        if (power.PowerType.Length > 0)
        {
            coreSignals++;
        }

        power.StackType = ReadEnumValue(source, "PowerStackType");
        if (power.StackType.Length > 0)
        {
            coreSignals++;
        }

        power.IsPlayerPower = ReadBoolValue(source, "IsPlayerPower", true);

        IReadOnlyList<string> hooks = SourcePatterns.GetHookMethodNames(source);
        power.HookNames = hooks;
        power.HookSource = JoinHookSources(source, hooks, ref roslynFallbackUsed);
        if (hooks.Count > 0)
        {
            coreSignals++;
        }

        power.RoslynFallbackUsed = roslynFallbackUsed;
        power.ConfidenceScore = coreSignals / 4m;
        return power;
    }

    private static string JoinHookSources(string source, IReadOnlyList<string> hooks, ref bool roslynFallbackUsed)
    {
        List<string> blocks = new List<string>();
        for (int i = 0; i < hooks.Count; i++)
        {
            string hook = hooks[i];
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

    private static bool ReadBoolValue(string source, string propertyName, bool fallback)
    {
        Match match = Regex.Match(source, propertyName + @"\s*=>\s*(?<value>true|false)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return fallback;
        }

        return string.Equals(match.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sts2Extractor.Parsing;

internal static class SourcePatterns
{
    private static readonly Regex ClassNameRegex = new Regex(@"\bclass\s+(?<name>\w+)\s*:\s*(?<base>[\w\.<>]+)", RegexOptions.Compiled);
    private static readonly Regex BaseCtorRegex = new Regex(@":\s*base\((?<args>[^\)]*)\)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex MethodSignatureRegex = new Regex(@"(?<sig>(public|protected|private)\s+override\s+[\w\<\>\[\]\.,\s]+\s+(?<name>\w+)\s*\([^\)]*\)\s*)\{", RegexOptions.Compiled);
    private static readonly Regex CanonicalKeywordsRegex = new Regex(@"CanonicalKeywords\s*=>\s*(?<body>[\s\S]*?);", RegexOptions.Compiled);
    private static readonly Regex CardKeywordRegex = new Regex(@"CardKeyword\.(?<keyword>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

    public static bool TryGetClassInfo(string source, out string className, out string baseType)
    {
        return TryGetClassInfo(source, out className, out baseType, out _);
    }

    public static bool TryGetClassInfo(string source, out string className, out string baseType, out bool roslynFallbackUsed)
    {
        Match match = ClassNameRegex.Match(source);
        if (match.Success)
        {
            className = match.Groups["name"].Value;
            baseType = NormalizeTypeName(match.Groups["base"].Value);
            roslynFallbackUsed = false;
            return className.Length > 0;
        }

        bool parsed = RoslynSourceParser.TryGetClassInfo(source, out className, out baseType);
        roslynFallbackUsed = parsed;
        return parsed;
    }

    public static bool TryGetBaseConstructorArgs(string source, out string[] args)
    {
        return TryGetBaseConstructorArgs(source, out args, out _);
    }

    public static bool TryGetBaseConstructorArgs(string source, out string[] args, out bool roslynFallbackUsed)
    {
        Match match = BaseCtorRegex.Match(source);
        if (match.Success)
        {
            args = match.Groups["args"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(static part => part.Trim())
                .ToArray();

            if (args.Length > 0)
            {
                roslynFallbackUsed = false;
                return true;
            }
        }

        bool parsed = RoslynSourceParser.TryGetBaseConstructorArgs(source, out args);
        roslynFallbackUsed = parsed;
        return parsed;
    }

    public static IReadOnlyList<string> ReadCardKeywords(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new List<string>();
        }

        Match match = CanonicalKeywordsRegex.Match(source);
        if (!match.Success)
        {
            return new List<string>();
        }

        string keywordSource = match.Groups["body"].Value;
        MatchCollection keywordMatches = CardKeywordRegex.Matches(keywordSource);
        HashSet<string> keywords = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < keywordMatches.Count; i++)
        {
            string keyword = keywordMatches[i].Groups["keyword"].Value.Trim();
            if (keyword.Length > 0)
            {
                keywords.Add(keyword);
            }
        }

        return keywords.ToList();
    }

    public static bool ContainsCanonicalVars(string source)
    {
        return source.Contains("CanonicalVars", StringComparison.Ordinal);
    }

    public static bool ContainsGainsBlock(string source)
    {
        return source.Contains("GainsBlock", StringComparison.Ordinal);
    }

    public static bool ContainsOnUpgrade(string source)
    {
        return source.Contains("OnUpgrade(", StringComparison.Ordinal);
    }

    public static bool ContainsOnPlay(string source)
    {
        return source.Contains("OnPlay(", StringComparison.Ordinal);
    }

    public static bool ContainsRarity(string source)
    {
        return source.Contains("Rarity =>", StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> GetHookMethodNames(string source)
    {
        HashSet<string> hooks = new HashSet<string>(StringComparer.Ordinal);
        MatchCollection matches = MethodSignatureRegex.Matches(source);
        foreach (Match match in matches)
        {
            string name = match.Groups["name"].Value;
            if (name.StartsWith("On", StringComparison.Ordinal) ||
                name.StartsWith("Before", StringComparison.Ordinal) ||
                name.StartsWith("After", StringComparison.Ordinal) ||
                name.StartsWith("Modify", StringComparison.Ordinal))
            {
                hooks.Add(name);
            }
        }

        foreach (string hook in RoslynSourceParser.GetOverrideMethodNames(source))
        {
            hooks.Add(hook);
        }

        return hooks.ToList();
    }

    public static string ExtractMethodBlock(string source, string methodName)
    {
        return ExtractMethodBlock(source, methodName, out _);
    }

    public static string ExtractMethodBlock(string source, string methodName, out bool roslynFallbackUsed)
    {
        int nameIndex = source.IndexOf(methodName + "(", StringComparison.Ordinal);
        if (nameIndex >= 0)
        {
            int braceStart = source.IndexOf('{', nameIndex);
            if (braceStart >= 0)
            {
                int depth = 0;
                for (int i = braceStart; i < source.Length; i++)
                {
                    char c = source[i];
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            roslynFallbackUsed = false;
                            return source.Substring(braceStart, i - braceStart + 1);
                        }
                    }
                }
            }
        }

        string extracted = RoslynSourceParser.ExtractMethodBodyOrExpression(source, methodName);
        roslynFallbackUsed = extracted.Length > 0;
        return extracted;
    }

    public static int ReadIntLiteral(string source, string pattern)
    {
        Match match = Regex.Match(source, pattern, RegexOptions.Singleline);
        if (!match.Success)
        {
            return int.MinValue;
        }

        string raw = match.Groups["value"].Value.Trim();
        if (raw.EndsWith("m", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(0, raw.Length - 1);
        }

        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return decimal.ToInt32(decimal.Truncate(parsed));
        }

        return int.MinValue;
    }

    public static bool IsTypeName(string actualType, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(actualType) || string.IsNullOrWhiteSpace(expectedType))
        {
            return false;
        }

        return string.Equals(NormalizeTypeName(actualType), NormalizeTypeName(expectedType), StringComparison.Ordinal);
    }

    private static string NormalizeTypeName(string typeName)
    {
        string normalized = typeName.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        int genericStart = normalized.IndexOf('<');
        if (genericStart >= 0)
        {
            normalized = normalized.Substring(0, genericStart);
        }

        int namespaceSeparator = normalized.LastIndexOf('.');
        if (namespaceSeparator >= 0 && namespaceSeparator < normalized.Length - 1)
        {
            normalized = normalized.Substring(namespaceSeparator + 1);
        }

        return normalized;
    }
}

using System.Collections.Generic;
using Sts2Extractor.Parsing;

namespace Sts2Extractor.Audit;

internal sealed class PatternInspector
{
    public PatternAuditRecord Inspect(string relativePath, string source)
    {
        if (relativePath.Contains(".Models.Cards", System.StringComparison.OrdinalIgnoreCase))
        {
            return InspectCard(relativePath, source);
        }

        if (relativePath.Contains(".Models.Relics", System.StringComparison.OrdinalIgnoreCase))
        {
            return InspectRelic(relativePath, source);
        }

        return InspectPower(relativePath, source);
    }

    private static PatternAuditRecord InspectCard(string relativePath, string source)
    {
        List<string> found = new List<string>();
        List<string> missing = new List<string>();

        if (SourcePatterns.TryGetClassInfo(source, out _, out string baseType) && SourcePatterns.IsTypeName(baseType, "CardModel")) found.Add("class_name"); else missing.Add("class_name");
        if (SourcePatterns.TryGetBaseConstructorArgs(source, out string[] args) && args.Length >= 4) found.Add("base_constructor"); else missing.Add("base_constructor");
        if (SourcePatterns.ContainsCanonicalVars(source)) found.Add("canonical_vars"); else missing.Add("canonical_vars");
        if (SourcePatterns.ContainsOnPlay(source)) found.Add("on_play"); else missing.Add("on_play");
        if (SourcePatterns.ContainsOnUpgrade(source)) found.Add("on_upgrade"); else missing.Add("on_upgrade");

        decimal confidence = found.Count / 5m;
        return new PatternAuditRecord
        {
            FilePath = relativePath,
            EntityType = EntityKind.Card,
            FieldsFound = found,
            FieldsMissing = missing,
            ConfidenceScore = confidence
        };
    }

    private static PatternAuditRecord InspectRelic(string relativePath, string source)
    {
        List<string> found = new List<string>();
        List<string> missing = new List<string>();

        if (SourcePatterns.TryGetClassInfo(source, out _, out string baseType) && SourcePatterns.IsTypeName(baseType, "RelicModel")) found.Add("class_name"); else missing.Add("class_name");
        if (SourcePatterns.ContainsRarity(source)) found.Add("rarity"); else missing.Add("rarity");
        if (SourcePatterns.ContainsCanonicalVars(source)) found.Add("canonical_vars"); else missing.Add("canonical_vars");
        if (SourcePatterns.GetHookMethodNames(source).Count > 0) found.Add("hook_methods"); else missing.Add("hook_methods");

        decimal confidence = found.Count / 4m;
        return new PatternAuditRecord
        {
            FilePath = relativePath,
            EntityType = EntityKind.Relic,
            FieldsFound = found,
            FieldsMissing = missing,
            ConfidenceScore = confidence
        };
    }

    private static PatternAuditRecord InspectPower(string relativePath, string source)
    {
        List<string> found = new List<string>();
        List<string> missing = new List<string>();

        bool hasClassName = SourcePatterns.TryGetClassInfo(source, out _, out string baseType) && SourcePatterns.IsTypeName(baseType, "PowerModel");
        bool hasPowerType = source.Contains("PowerType.", System.StringComparison.Ordinal);
        bool hasPowerStackType = source.Contains("PowerStackType.", System.StringComparison.Ordinal);
        bool hasCanonicalVars = SourcePatterns.ContainsCanonicalVars(source);
        bool hasHooks = SourcePatterns.GetHookMethodNames(source).Count > 0;

        if (hasClassName) found.Add("class_name"); else missing.Add("class_name");
        if (hasPowerType) found.Add("power_type"); else missing.Add("power_type");
        if (hasPowerStackType) found.Add("power_stack_type"); else missing.Add("power_stack_type");
        if (hasCanonicalVars) found.Add("canonical_vars"); else missing.Add("canonical_vars");
        if (hasHooks) found.Add("hook_methods"); else missing.Add("hook_methods");

        int coreSignals = 0;
        if (hasClassName) coreSignals++;
        if (hasPowerType) coreSignals++;
        if (hasPowerStackType) coreSignals++;
        if (hasHooks) coreSignals++;

        decimal confidence = coreSignals / 4m;
        return new PatternAuditRecord
        {
            FilePath = relativePath,
            EntityType = EntityKind.Power,
            FieldsFound = found,
            FieldsMissing = missing,
            ConfidenceScore = confidence
        };
    }
}

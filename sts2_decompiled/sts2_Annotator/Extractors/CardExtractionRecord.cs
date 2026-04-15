using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class CardExtractionRecord
{
    public string FilePath { get; set; }

    public string CardId { get; set; }

    public string IdResolutionStatus { get; set; }

    public bool RoslynFallbackUsed { get; set; }

    public int EnergyCost { get; set; }

    public string CardType { get; set; }

    public string CardRarity { get; set; }

    public string TargetType { get; set; }

    public bool GainsBlock { get; set; }

    public IReadOnlyList<string> Keywords { get; set; }

    public int MaxUpgradeLevel { get; set; }

    public int DamageBase { get; set; }

    public int DamageUpgraded { get; set; }

    public int BlockBase { get; set; }

    public int BlockUpgraded { get; set; }

    public int MagicBase { get; set; }

    public int MagicUpgraded { get; set; }

    public decimal ConfidenceScore { get; set; }

    public string OnPlaySource { get; set; }

    public string OnUpgradeSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public IReadOnlyList<CompanionActionRecord> CompanionActions { get; set; }

    public CardExtractionRecord()
    {
        FilePath = string.Empty;
        CardId = string.Empty;
        IdResolutionStatus = string.Empty;
        CardType = string.Empty;
        CardRarity = string.Empty;
        TargetType = string.Empty;
        Keywords = new List<string>();
        MaxUpgradeLevel = 1;
        DamageBase = int.MinValue;
        DamageUpgraded = int.MinValue;
        BlockBase = int.MinValue;
        BlockUpgraded = int.MinValue;
        MagicBase = int.MinValue;
        MagicUpgraded = int.MinValue;
        OnPlaySource = string.Empty;
        OnUpgradeSource = string.Empty;
        EffectTags = new List<string>();
        CompanionActions = new List<CompanionActionRecord>();
    }
}

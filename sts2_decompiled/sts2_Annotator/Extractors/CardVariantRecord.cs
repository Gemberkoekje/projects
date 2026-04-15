using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

/// <summary>
/// Represents a card variant (exception to base/upgraded structure).
/// Used for enchantments, afflictions, or other non-standard states.
/// </summary>
internal sealed class CardVariantRecord
{
    public string CardId { get; set; }

    public string VariantKind { get; set; }

    public int? UpgradeLevel { get; set; }

    public string SourceId { get; set; }

    public int? EnergyCost { get; set; }

    public int? DamageValue { get; set; }

    public int? BlockValue { get; set; }

    public int? MagicValue { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public IReadOnlyList<string> ResourcesGenerated { get; set; }

    public IReadOnlyList<string> ResourcesConsumed { get; set; }

    public string ScalingType { get; set; }

    public IReadOnlyList<string> AntiSynergyTags { get; set; }

    public CardVariantRecord()
    {
        CardId = string.Empty;
        VariantKind = string.Empty;
        SourceId = string.Empty;
        EffectDescription = string.Empty;
        EffectTags = new List<string>();
        ResourcesGenerated = new List<string>();
        ResourcesConsumed = new List<string>();
        ScalingType = string.Empty;
        AntiSynergyTags = new List<string>();
    }
}

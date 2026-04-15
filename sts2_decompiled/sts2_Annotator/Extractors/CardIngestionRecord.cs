using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

/// <summary>
/// Represents a card ready for ingestion into the cards table.
/// Only uses base/upgraded columns; variants are split to CardVariantRecord.
/// </summary>
internal sealed class CardIngestionRecord
{
    public string CardId { get; set; }

    /// <summary>
    /// The character this card belongs to, or empty string for full-scan runs.
    /// "colorless" maps to NULL in the database (universally draftable).
    /// </summary>
    public string CharacterId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Type { get; set; }

    public string Rarity { get; set; }

    public int? EnergyCost { get; set; }

    public string Target { get; set; }

    public bool GainsBlock { get; set; }

    public IReadOnlyList<string> Keywords { get; set; }

    public int? DamageBase { get; set; }

    public int? DamageUpgraded { get; set; }

    public int? BlockBase { get; set; }

    public int? BlockUpgraded { get; set; }

    public int? MagicBase { get; set; }

    public int? MagicUpgraded { get; set; }

    public int MaxUpgrade { get; set; }

    public string OnPlaySource { get; set; }

    public string OnUpgradeSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public IReadOnlyList<string> ResourcesGenerated { get; set; }

    public IReadOnlyList<string> ResourcesConsumed { get; set; }

    public string ScalingType { get; set; }

    public IReadOnlyList<string> AntiSynergyTags { get; set; }

    public CardIngestionRecord()
    {
        CardId = string.Empty;
        CharacterId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Type = string.Empty;
        Rarity = string.Empty;
        Target = string.Empty;
        OnPlaySource = string.Empty;
        OnUpgradeSource = string.Empty;
        EffectDescription = string.Empty;
        Keywords = new List<string>();
        EffectTags = new List<string>();
        ResourcesGenerated = new List<string>();
        ResourcesConsumed = new List<string>();
        ScalingType = string.Empty;
        AntiSynergyTags = new List<string>();
        MaxUpgrade = 1;
    }
}

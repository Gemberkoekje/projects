using System;
using System.Collections.Generic;
using Sts2Extractor.Localization;

namespace Sts2Extractor.Extractors;

/// <summary>
/// Converts CardExtractionRecord output (raw parsed data) into:
/// 1. CardIngestionRecord (base/upgraded only, ready for cards table)
/// 2. CardVariantRecord (exceptions, ready for card_variants table)
/// 
/// Strategy:
/// - All cards use base/upgraded columns by default.
/// - Only emit variants for non-standard cases (e.g., multi-upgrade cards, enchantments).
/// - For now, STS2 primarily uses MaxUpgrade=0 or MaxUpgrade=1, so variants are rare.
/// </summary>
internal sealed class CardIngestionConverter
{
    private readonly LocStringReader _locReader;

    public CardIngestionConverter(LocStringReader locReader)
    {
        _locReader = locReader;
    }

    /// <summary>
    /// Converts an extraction record into ingestion record(s).
    /// </summary>
    /// <param name="extraction">The raw parsed extraction record.</param>
    /// <param name="characterId">The character scope from CLI options; empty string for full-scan runs.</param>
    public CardIngestionRecord ToIngestionRecord(CardExtractionRecord extraction, string characterId = "")
    {
        if (extraction is null)
            throw new ArgumentNullException(nameof(extraction));

        return new CardIngestionRecord
        {
            CardId = extraction.CardId,
            CharacterId = characterId ?? string.Empty,
            Title = _locReader.GetCardTitle(extraction.CardId),
            Description = _locReader.GetCardDescription(extraction.CardId),
            Type = extraction.CardType,
            Rarity = extraction.CardRarity,
            EnergyCost = extraction.EnergyCost == int.MinValue ? null : extraction.EnergyCost,
            Target = extraction.TargetType,
            GainsBlock = extraction.GainsBlock,
            Keywords = extraction.Keywords,
            DamageBase = extraction.DamageBase == int.MinValue ? null : extraction.DamageBase,
            DamageUpgraded = extraction.DamageUpgraded == int.MinValue ? null : extraction.DamageUpgraded,
            BlockBase = extraction.BlockBase == int.MinValue ? null : extraction.BlockBase,
            BlockUpgraded = extraction.BlockUpgraded == int.MinValue ? null : extraction.BlockUpgraded,
            MagicBase = extraction.MagicBase == int.MinValue ? null : extraction.MagicBase,
            MagicUpgraded = extraction.MagicUpgraded == int.MinValue ? null : extraction.MagicUpgraded,
            MaxUpgrade = extraction.MaxUpgradeLevel,
            OnPlaySource = extraction.OnPlaySource,
            OnUpgradeSource = extraction.OnUpgradeSource,
            EffectTags = extraction.EffectTags,
            EffectDescription = string.Empty,  // Will be populated by LLM annotation
            ResourcesGenerated = new List<string>(),  // Will be populated by LLM annotation
            ResourcesConsumed = new List<string>(),  // Will be populated by LLM annotation
            ScalingType = string.Empty,  // Will be populated by LLM annotation
            AntiSynergyTags = new List<string>()  // Will be populated by LLM annotation
        };
    }

    /// <summary>
    /// Extracts variants from an ingestion record if MaxUpgrade > 1.
    /// Currently, STS2 uses mostly MaxUpgrade=0 (unupgradable) or MaxUpgrade=1 (standard),
    /// so this typically returns an empty list.
    /// </summary>
    public IReadOnlyList<CardVariantRecord> ExtractVariants(string cardId, int maxUpgradeLevel)
    {
        List<CardVariantRecord> variants = new List<CardVariantRecord>();

        // STS2 doesn't typically have multi-upgrade cards, but if they do,
        // they'd be handled as separate variant records for levels > 1.
        // For now, this is a placeholder for future non-standard upgrades
        // (e.g., enchantments, afflictions, temporary states).

        return variants;
    }
}

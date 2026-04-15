using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class PotionIngestionRecord
{
    public string PotionId { get; set; }

    public string CharacterId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Rarity { get; set; }

    public int AmountBase { get; set; }

    public string OnUseSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public PotionIngestionRecord()
    {
        PotionId = string.Empty;
        CharacterId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Rarity = string.Empty;
        AmountBase = int.MinValue;
        OnUseSource = string.Empty;
        EffectTags = new List<string>();
        EffectDescription = string.Empty;
    }
}

using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class RelicIngestionRecord
{
    public string RelicId { get; set; }

    public string CharacterId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string FlavorText { get; set; }

    public string Rarity { get; set; }

    public int CounterBase { get; set; }

    public int AmountBase { get; set; }

    public string HookSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public IReadOnlyList<string> TriggersOn { get; set; }

    public string TriggerCondition { get; set; }

    public IReadOnlyList<string> ResourcesGenerated { get; set; }

    public IReadOnlyList<string> ResourcesConsumed { get; set; }

    public RelicIngestionRecord()
    {
        RelicId = string.Empty;
        CharacterId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        FlavorText = string.Empty;
        Rarity = string.Empty;
        CounterBase = int.MinValue;
        AmountBase = int.MinValue;
        HookSource = string.Empty;
        EffectTags = new List<string>();
        EffectDescription = string.Empty;
        TriggersOn = new List<string>();
        TriggerCondition = string.Empty;
        ResourcesGenerated = new List<string>();
        ResourcesConsumed = new List<string>();
    }
}

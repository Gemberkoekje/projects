using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class PowerIngestionRecord
{
    public string PowerId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public string Type { get; set; }

    public string StackType { get; set; }

    public bool IsPlayerPower { get; set; }

    public string HookSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public IReadOnlyList<string> TriggersOn { get; set; }

    public string ScalingType { get; set; }

    public PowerIngestionRecord()
    {
        PowerId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        Type = string.Empty;
        StackType = string.Empty;
        HookSource = string.Empty;
        EffectTags = new List<string>();
        EffectDescription = string.Empty;
        TriggersOn = new List<string>();
        ScalingType = string.Empty;
        IsPlayerPower = true;
    }
}

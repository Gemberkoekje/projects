using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class EventIngestionRecord
{
    public string EventId { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public bool IsDeterministic { get; set; }

    public string OptionsSummary { get; set; }

    public string OutcomeSource { get; set; }

    public IReadOnlyList<string> EffectTags { get; set; }

    public string EffectDescription { get; set; }

    public EventIngestionRecord()
    {
        EventId = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
        OptionsSummary = string.Empty;
        OutcomeSource = string.Empty;
        EffectTags = new List<string>();
        EffectDescription = string.Empty;
    }
}

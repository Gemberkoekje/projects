namespace Sts2Extractor.Extractors;

internal sealed class EventOutcomeRecord
{
    public string EventId { get; set; }

    public string OutcomeId { get; set; }

    public string OutcomeKind { get; set; }

    public string OutcomeSource { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public EventOutcomeRecord()
    {
        EventId = string.Empty;
        OutcomeId = string.Empty;
        OutcomeKind = string.Empty;
        OutcomeSource = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
    }
}

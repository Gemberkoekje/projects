namespace Sts2Extractor.Extractors;

internal sealed class EventOptionRecord
{
    public string EventId { get; set; }

    public string OptionTextKey { get; set; }

    public string HandlerMethod { get; set; }

    public string HandlerSource { get; set; }

    public string Title { get; set; }

    public string Description { get; set; }

    public EventOptionRecord()
    {
        EventId = string.Empty;
        OptionTextKey = string.Empty;
        HandlerMethod = string.Empty;
        HandlerSource = string.Empty;
        Title = string.Empty;
        Description = string.Empty;
    }
}

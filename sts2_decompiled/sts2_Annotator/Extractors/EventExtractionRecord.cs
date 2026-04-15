using System.Collections.Generic;

namespace Sts2Extractor.Extractors;

internal sealed class EventExtractionRecord
{
    public string FilePath { get; set; }

    public string EventId { get; set; }

    public string IdResolutionStatus { get; set; }

    public bool RoslynFallbackUsed { get; set; }

    public bool IsShared { get; set; }

    public string InitialOptionsSource { get; set; }

    public string ResumeSource { get; set; }

    public IReadOnlyList<EventOptionRecord> Options { get; set; }

    public IReadOnlyList<EventOutcomeRecord> Outcomes { get; set; }

    public decimal ConfidenceScore { get; set; }

    public EventExtractionRecord()
    {
        FilePath = string.Empty;
        EventId = string.Empty;
        IdResolutionStatus = string.Empty;
        InitialOptionsSource = string.Empty;
        ResumeSource = string.Empty;
        Options = new List<EventOptionRecord>();
        Outcomes = new List<EventOutcomeRecord>();
    }
}

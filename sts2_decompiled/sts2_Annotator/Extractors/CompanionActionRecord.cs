namespace Sts2Extractor.Extractors;

internal sealed class CompanionActionRecord
{
    public string CardId { get; set; }

    public string CompanionId { get; set; }

    public string ActionTag { get; set; }

    public string SourceMethod { get; set; }

    public CompanionActionRecord()
    {
        CardId = string.Empty;
        CompanionId = string.Empty;
        ActionTag = string.Empty;
        SourceMethod = string.Empty;
    }
}

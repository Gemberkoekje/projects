namespace Sts2Extractor.Audit;

internal sealed class PatternAuditResult
{
    public string OutputPath { get; set; }

    public int RecordCount { get; set; }

    public int FallbackRequiredCount { get; set; }

    public PatternAuditResult()
    {
        OutputPath = string.Empty;
    }
}

namespace Sts2Extractor.Extractors;

internal sealed class PowerExtractionResult
{
    public string OutputPath { get; set; }

    public string IngestionOutputPath { get; set; }

    public string HooksOutputPath { get; set; }

    public int RecordCount { get; set; }

    public int HookCount { get; set; }

    public int LowConfidenceCount { get; set; }

    public PowerExtractionResult()
    {
        OutputPath = string.Empty;
        IngestionOutputPath = string.Empty;
        HooksOutputPath = string.Empty;
    }
}

namespace Sts2Extractor.Extractors;

internal sealed class PotionExtractionResult
{
    public string OutputPath { get; set; }

    public string IngestionOutputPath { get; set; }

    public int RecordCount { get; set; }

    public int LowConfidenceCount { get; set; }

    public PotionExtractionResult()
    {
        OutputPath = string.Empty;
        IngestionOutputPath = string.Empty;
    }
}

namespace Sts2Extractor.Extractors;

internal sealed class CardExtractionResult
{
    public string OutputPath { get; set; }

    public string CompanionOutputPath { get; set; }

    public string IngestionOutputPath { get; set; }

    public string VariantOutputPath { get; set; }

    public int RecordCount { get; set; }

    public int LowConfidenceCount { get; set; }

    public int CompanionActionCount { get; set; }

    public int VariantCount { get; set; }

    public CardExtractionResult()
    {
        OutputPath = string.Empty;
        CompanionOutputPath = string.Empty;
        IngestionOutputPath = string.Empty;
        VariantOutputPath = string.Empty;
    }
}

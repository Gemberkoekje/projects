namespace Sts2Extractor.Extractors;

internal sealed class EventExtractionResult
{
    public string OutputPath { get; set; }

    public string IngestionOutputPath { get; set; }

    public string OptionsOutputPath { get; set; }

    public string OutcomesOutputPath { get; set; }

    public int RecordCount { get; set; }

    public int OptionCount { get; set; }

    public int OutcomeCount { get; set; }

    public int LowConfidenceCount { get; set; }

    public EventExtractionResult()
    {
        OutputPath = string.Empty;
        IngestionOutputPath = string.Empty;
        OptionsOutputPath = string.Empty;
        OutcomesOutputPath = string.Empty;
    }
}

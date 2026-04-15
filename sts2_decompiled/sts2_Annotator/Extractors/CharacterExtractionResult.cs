namespace Sts2Extractor.Extractors;

internal sealed class CharacterExtractionResult
{
    public string OutputPath { get; set; }

    public string IngestionOutputPath { get; set; }

    public int RecordCount { get; set; }

    public CharacterExtractionResult()
    {
        OutputPath = string.Empty;
        IngestionOutputPath = string.Empty;
    }
}

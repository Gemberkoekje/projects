namespace Sts2Extractor.Taxonomy;

internal sealed class TaxonomySeedResult
{
    public string OutputPath { get; set; }

    public int RecordCount { get; set; }

    public TaxonomySeedResult()
    {
        OutputPath = string.Empty;
    }
}

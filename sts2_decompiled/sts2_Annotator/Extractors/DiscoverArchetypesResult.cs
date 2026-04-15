namespace Sts2Extractor.Extractors;

internal sealed class DiscoverArchetypesResult
{
    public string OutputPath { get; set; }

    public int CharacterCount { get; set; }

    public int ArchetypeCount { get; set; }

    public DiscoverArchetypesResult()
    {
        OutputPath = string.Empty;
    }
}

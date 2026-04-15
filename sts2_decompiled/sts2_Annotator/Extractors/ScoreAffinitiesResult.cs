namespace Sts2Extractor.Extractors;

internal sealed class ScoreAffinitiesResult
{
    public string OutputPath { get; set; }

    public int CharacterCount { get; set; }

    public int EntityCount { get; set; }

    public int AffinityCount { get; set; }

    public int BatchCount { get; set; }

    public ScoreAffinitiesResult()
    {
        OutputPath = string.Empty;
    }
}

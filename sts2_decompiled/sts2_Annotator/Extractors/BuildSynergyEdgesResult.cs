namespace Sts2Extractor.Extractors;

internal sealed class BuildSynergyEdgesResult
{
    public string OutputPath { get; set; }

    public int EdgeCount { get; set; }

    public int CandidatePairCount { get; set; }

    public int BatchCount { get; set; }

    public BuildSynergyEdgesResult()
    {
        OutputPath = string.Empty;
    }
}

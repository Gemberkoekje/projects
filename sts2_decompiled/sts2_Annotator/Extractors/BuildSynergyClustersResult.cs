namespace Sts2Extractor.Extractors;

internal sealed class BuildSynergyClustersResult
{
    public string ArchetypesOutputPath { get; set; }

    public string ClustersOutputPath { get; set; }

    public int ArchetypeCount { get; set; }

    public int ClusterCount { get; set; }

    public BuildSynergyClustersResult()
    {
        ArchetypesOutputPath = string.Empty;
        ClustersOutputPath = string.Empty;
    }
}

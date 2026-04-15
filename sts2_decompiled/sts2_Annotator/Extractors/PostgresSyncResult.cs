namespace Sts2Extractor.Extractors;

internal sealed class PostgresSyncResult
{
    public int InsertedRowCount { get; set; }

    public int AnnotatedEntityCount { get; set; }

    public int RatingsCount { get; set; }

    public int ArchetypeCount { get; set; }

    public int SynergyClusterCount { get; set; }

    public int AffinityCount { get; set; }

    public int SynergyEdgeCount { get; set; }

    public int SynergyEdgeCandidatePairCount { get; set; }

    public int SynergyEdgeBatchCount { get; set; }

    public string ConnectionDisplay { get; set; }

    public string StagingDirectory { get; set; }

    public PostgresSyncResult()
    {
        ConnectionDisplay = string.Empty;
        StagingDirectory = string.Empty;
    }
}

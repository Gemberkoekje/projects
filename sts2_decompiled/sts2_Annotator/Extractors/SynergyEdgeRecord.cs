namespace Sts2Extractor.Extractors;

internal sealed class SynergyEdgeRecord
{
    public string EntityAType { get; set; }

    public string EntityAId { get; set; }

    public string EntityBType { get; set; }

    public string EntityBId { get; set; }

    public int SynergyStrength { get; set; }

    public bool IsAntiSynergy { get; set; }

    public string SharedTags { get; set; }

    public string Explanation { get; set; }

    public SynergyEdgeRecord()
    {
        EntityAType = string.Empty;
        EntityAId = string.Empty;
        EntityBType = string.Empty;
        EntityBId = string.Empty;
        SharedTags = string.Empty;
        Explanation = string.Empty;
    }
}

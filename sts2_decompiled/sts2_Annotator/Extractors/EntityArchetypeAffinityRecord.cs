namespace Sts2Extractor.Extractors;

internal sealed class EntityArchetypeAffinityRecord
{
    public int ArchetypeId { get; set; }

    public string CharacterId { get; set; }

    public string ArchetypeName { get; set; }

    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public int AffinityScore { get; set; }

    public EntityArchetypeAffinityRecord()
    {
        CharacterId = string.Empty;
        ArchetypeName = string.Empty;
        EntityType = string.Empty;
        EntityId = string.Empty;
    }
}

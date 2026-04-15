namespace Sts2Viewer.Data;

public sealed class EntityArchetypeAffinityRow
{
    public int ArchetypeId { get; set; }

    public string ArchetypeName { get; set; }

    public int AffinityScore { get; set; }

    public string CharacterId { get; set; }

    public EntityArchetypeAffinityRow()
    {
        ArchetypeName = string.Empty;
        CharacterId = string.Empty;
    }
}

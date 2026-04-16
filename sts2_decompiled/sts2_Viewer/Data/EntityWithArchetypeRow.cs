namespace Sts2Viewer.Data;

public sealed class EntityWithArchetypeRow
{
    public EntityRow Entity { get; set; }

    public EntityArchetypeAffinityRow HighestSynergyArchetype { get; set; }

    public EntityWithArchetypeRow()
    {
        Entity = new EntityRow();
        HighestSynergyArchetype = new EntityArchetypeAffinityRow();
    }
}

using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class EntitySynergyEdgeRow
{
    public string EntityAType { get; set; }

    public string EntityAId { get; set; }

    public string EntityBType { get; set; }

    public string EntityBId { get; set; }

    public int SynergyStrength { get; set; }

    public bool IsAntiSynergy { get; set; }

    public IReadOnlyList<string> SharedTags { get; set; }

    public string Explanation { get; set; }

    public EntitySynergyEdgeRow()
    {
        EntityAType = string.Empty;
        EntityAId = string.Empty;
        EntityBType = string.Empty;
        EntityBId = string.Empty;
        SharedTags = new List<string>();
        Explanation = string.Empty;
    }
}

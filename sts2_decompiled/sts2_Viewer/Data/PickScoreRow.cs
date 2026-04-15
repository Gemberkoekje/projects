using System.Collections.Generic;

namespace Sts2Viewer.Data;

public sealed class PickScoreRow
{
    public int Rank { get; set; }

    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public double EdgeOverlapScore { get; set; }

    public double ArchetypeFitScore { get; set; }

    public double AntiSynergyPenalty { get; set; }

    public double FlexibilityScore { get; set; }

    public double CompositeScore { get; set; }

    public IReadOnlyList<string> SynergyDrivers { get; set; }

    public string Explanation { get; set; }

    public PickScoreRow()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        SynergyDrivers = new List<string>();
        Explanation = string.Empty;
    }
}

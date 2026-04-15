using System;

namespace Sts2Viewer.Data;

public sealed class PickAdviceRow
{
    public int Id { get; set; }

    public int RunStateId { get; set; }

    public string OptionEntityType { get; set; }

    public string OptionEntityId { get; set; }

    public int Rank { get; set; }

    public double EdgeOverlapScore { get; set; }

    public double ArchetypeFitScore { get; set; }

    public double AntiSynergyPenalty { get; set; }

    public double CompositeScore { get; set; }

    public string Explanation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public PickAdviceRow()
    {
        OptionEntityType = string.Empty;
        OptionEntityId = string.Empty;
        Explanation = string.Empty;
    }
}

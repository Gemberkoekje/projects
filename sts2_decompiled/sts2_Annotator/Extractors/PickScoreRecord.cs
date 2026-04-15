namespace Sts2Extractor.Extractors;

internal sealed class PickScoreRecord
{
    public int Rank { get; set; }

    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public double EdgeOverlapScore { get; set; }

    public double ArchetypeFitScore { get; set; }

    public double AntiSynergyPenalty { get; set; }

    public double FlexibilityScore { get; set; }

    public double CompositeScore { get; set; }

    public string SynergyDrivers { get; set; }

    public string Explanation { get; set; }

    public PickScoreRecord()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        SynergyDrivers = string.Empty;
        Explanation = string.Empty;
    }
}

namespace Sts2Extractor.Extractors;

internal sealed class EntityStrengthRatingsResult
{
    public string OutputPath { get; set; }

    public int RecordCount { get; set; }

    public int CardCount { get; set; }

    public int RelicCount { get; set; }

    public int PotionCount { get; set; }

    public int EventOptionCount { get; set; }

    public EntityStrengthRatingsResult()
    {
        OutputPath = string.Empty;
    }
}

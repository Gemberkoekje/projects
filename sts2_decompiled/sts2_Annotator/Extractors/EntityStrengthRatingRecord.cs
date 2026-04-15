namespace Sts2Extractor.Extractors;

internal sealed class EntityStrengthRatingRecord
{
    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public string CardVariantId { get; set; }

    public int SynergyRating { get; set; }

    public int FlexibilityRating { get; set; }

    public int AntiSynergyRating { get; set; }

    public string Rationale { get; set; }

    public EntityStrengthRatingRecord()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        CardVariantId = string.Empty;
        Rationale = string.Empty;
    }
}

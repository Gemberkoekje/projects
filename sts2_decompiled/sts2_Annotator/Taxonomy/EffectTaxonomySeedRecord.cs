namespace Sts2Extractor.Taxonomy;

internal sealed class EffectTaxonomySeedRecord
{
    public string Tag { get; set; }

    public string Category { get; set; }

    public string Description { get; set; }

    public EffectTaxonomySeedRecord()
    {
        Tag = string.Empty;
        Category = string.Empty;
        Description = string.Empty;
    }
}

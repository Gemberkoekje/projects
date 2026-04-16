namespace Sts2Viewer.Data;

public sealed class EntityQueryRequest
{
    public string Search { get; set; }

    public string EntityType { get; set; }

    public string CharacterId { get; set; }

    public string Tag { get; set; }

    public string SortBy { get; set; }

    public string SortDirection { get; set; }

    public int MinSynergy { get; set; }

    public int MinFlexibility { get; set; }

    public int MaxAntiSynergy { get; set; }

    public EntityQueryRequest()
    {
        Search = string.Empty;
        EntityType = string.Empty;
        CharacterId = string.Empty;
        Tag = string.Empty;
        SortBy = "title";
        SortDirection = "asc";
        MinSynergy = 1;
        MinFlexibility = 1;
        MaxAntiSynergy = 10;
    }
}
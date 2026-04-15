namespace Sts2Viewer.Data;

public sealed class SynergyClusterRow
{
    public string ArchetypeName { get; set; }

    public string EntityType { get; set; }

    public string EntityId { get; set; }

    public string Role { get; set; }

    public decimal Score { get; set; }

    public string Explanation { get; set; }

    public SynergyClusterRow()
    {
        ArchetypeName = string.Empty;
        EntityType = string.Empty;
        EntityId = string.Empty;
        Role = string.Empty;
        Explanation = string.Empty;
    }
}
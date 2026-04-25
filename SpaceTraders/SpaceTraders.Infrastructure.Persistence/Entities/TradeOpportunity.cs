namespace SpaceTraders.Infrastructure.Persistence.Entities;

public sealed class TradeOpportunity
{
    public int Id { get; set; }

    public required string TradeSymbol { get; set; }

    public required string BuyWaypoint { get; set; }

    public required string SellWaypoint { get; set; }

    public int BuyPrice { get; set; }

    public int SellPrice { get; set; }

    public int ProfitPerUnit { get; set; }

    public int DistanceJumps { get; set; }

    public decimal ProfitPerJump { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}

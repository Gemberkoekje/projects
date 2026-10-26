using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// What a producer makes (§4.2.3). Output accrues to stock each heartbeat at a production cost
/// (a credit sink). Asking price falls as stock rises — oversupply depresses value (§4.2.2).
/// </summary>
public sealed record ProducerProfile(
    GoodType Good,
    decimal OutputPerHeartbeat,
    Credits UnitProductionCost,
    decimal StockCap,
    decimal Margin)
{
    /// <summary>How full the warehouse is, 0..1.</summary>
    public decimal Fullness(decimal currentStock) => StockCap <= 0m ? 1m : System.Math.Clamp(currentStock / StockCap, 0m, 1m);
}

/// <summary>
/// A station operator's terms (design §8.1): the storage rate per unit per day, and the float it keeps; rent
/// above the float goes on station upkeep (a sink, design §5.1).
/// </summary>
public sealed record StationProfile(Credits StorageRatePerUnitDay, Credits Float);

/// <summary>One good a consumer wants, with how fast it burns through it and the buffer it tries to keep.</summary>
public sealed record ConsumerDemand(
    GoodType Good,
    decimal ConsumptionPerHeartbeat,
    decimal TargetStock);

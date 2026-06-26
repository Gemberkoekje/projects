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
    /// <summary>An "idle" producer is at or near stock cap: a leading indicator of a price drop (§4.2.2, §3.3).</summary>
    public bool IsIdle(decimal currentStock) => currentStock >= StockCap * 0.92m;
}

/// <summary>One good a consumer wants, with how fast it burns through it and the buffer it tries to keep.</summary>
public sealed record ConsumerDemand(
    GoodType Good,
    decimal ConsumptionPerHeartbeat,
    decimal TargetStock);

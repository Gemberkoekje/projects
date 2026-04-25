using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces;

public interface ITradeAnalyser
{
    /// <summary>
    /// Computes all viable trade routes from the provided market data snapshots.
    /// Each market contains JSON-serialized trade goods with purchase and sell prices.
    /// </summary>
    IReadOnlyList<TradeOpportunityDto> ComputeOpportunities(
        IReadOnlyList<MarketSnapshot> markets,
        int minProfitPerUnit = 0);

    /// <summary>
    /// Selects the best route from a pre-computed list for the given ship constraints.
    /// </summary>
    TradeOpportunityDto? SelectBestRoute(
        IReadOnlyList<TradeOpportunityDto> routes,
        int cargoCapacity,
        int minProfitPerUnit,
        int maxDistanceJumps,
        bool prioritizeSupplyChain = true);
}

/// <summary>
/// Lightweight market snapshot used by the TradeAnalyser (no EF Core dependency).
/// </summary>
public sealed record MarketSnapshot(
    string WaypointSymbol,
    string SystemSymbol,
    IReadOnlyList<TradeGoodSnapshot> TradeGoods,
    IReadOnlyList<string> Imports,
    IReadOnlyList<string> Exports,
    IReadOnlyList<string> Exchange);

public sealed record TradeGoodSnapshot(
    string Symbol,
    string Type,
    int PurchasePrice,
    int SellPrice,
    int TradeVolume,
    string Supply);

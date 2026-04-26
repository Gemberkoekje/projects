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
public sealed record MarketSnapshot
{
    public required string WaypointSymbol { get; init; }

    public required string SystemSymbol { get; init; }

    public required IReadOnlyList<TradeGoodSnapshot> TradeGoods { get; init; }

    public required IReadOnlyList<string> Imports { get; init; }

    public required IReadOnlyList<string> Exports { get; init; }

    public required IReadOnlyList<string> Exchange { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketSnapshot(
        string waypointSymbol,
        string systemSymbol,
        IReadOnlyList<TradeGoodSnapshot> tradeGoods,
        IReadOnlyList<string> imports,
        IReadOnlyList<string> exports,
        IReadOnlyList<string> exchange)
    {
        this.WaypointSymbol = waypointSymbol;
        this.SystemSymbol = systemSymbol;
        this.TradeGoods = tradeGoods;
        this.Imports = imports;
        this.Exports = exports;
        this.Exchange = exchange;
    }
}

public sealed record TradeGoodSnapshot
{
    public required string Symbol { get; init; }

    public required string Type { get; init; }

    public required int PurchasePrice { get; init; }

    public required int SellPrice { get; init; }

    public required int TradeVolume { get; init; }

    public required string Supply { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeGoodSnapshot(
        string symbol,
        string type,
        int purchasePrice,
        int sellPrice,
        int tradeVolume,
        string supply)
    {
        this.Symbol = symbol;
        this.Type = type;
        this.PurchasePrice = purchasePrice;
        this.SellPrice = sellPrice;
        this.TradeVolume = tradeVolume;
        this.Supply = supply;
    }
}

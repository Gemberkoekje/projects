using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Trading;

/// <summary>Everything a trade decision reads: the system's map, the credits and the minimum profit.</summary>
public sealed record TradeContext
{
    /// <summary>Creates the context of one decision.</summary>
    /// <param name="Map">The ship's system: positions, markets and production chains.</param>
    /// <param name="Credits">The credits on hand, as cached.</param>
    /// <param name="MinProfitPerUnit">The profit per unit, after fuel, a trip must earn (D14).</param>
    /// <param name="FuelReserveCredits">The credits cargo must leave, so that fuel can always be bought (D24).</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TradeContext(TradeMarketMap Map, long Credits, int MinProfitPerUnit, long FuelReserveCredits = 0)
    {
        this.Map = Map;
        this.Credits = Credits;
        this.MinProfitPerUnit = MinProfitPerUnit;
        this.FuelReserveCredits = FuelReserveCredits;
    }

    /// <summary>The ship's system: positions, markets and production chains.</summary>
    public required TradeMarketMap Map { get; init; }

    /// <summary>The credits on hand, as cached.</summary>
    public required long Credits { get; init; }

    /// <summary>The profit per unit, after fuel, a trip must earn (D14).</summary>
    public required int MinProfitPerUnit { get; init; }

    /// <summary>
    /// The credits cargo must leave (D24, <c>Trade.FuelReserveCredits</c>): below them only fuel is bought,
    /// so a ship never holds cargo it can't afford to fly to its buyer.
    /// </summary>
    public required long FuelReserveCredits { get; init; }

    /// <summary>The credits a cargo purchase may use: those on hand above <see cref="FuelReserveCredits"/>.</summary>
    public long CreditsForCargo => Math.Max(0, Credits - FuelReserveCredits);
}

/// <summary>Reads the <see cref="TradeContext"/> for a system from the cache.</summary>
public interface ITradeContextReader
{
    /// <summary>Reads what a trade decision in a system needs.</summary>
    /// <param name="systemSymbol">The system the ship is in.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The system's map, the credits and the minimum profit per unit.</returns>
    Task<TradeContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <remarks>
/// Only the production chains may cost an API call, once per process (<see cref="ISupplyChainCache"/>);
/// everything else comes from the database. Without the chains no route counts as feeding production,
/// and routes are ranked by profit alone.
/// </remarks>
public sealed class TradeContextReader(
    IMarketRepository markets,
    IWaypointRepository waypoints,
    IAgentRepository agents,
    ISettingsRepository settings,
    ISupplyChainCache supplyChain,
    ISpaceTradersPort port) : ITradeContextReader
{
    /// <summary>The setting that holds the profit per unit, after fuel, a trip must earn (D14).</summary>
    public const string MinProfitPerUnitSetting = "Trade.MinProfitPerUnit";

    /// <summary>The setting that holds the credits cargo must leave, so fuel can always be bought (D24).</summary>
    public const string FuelReserveCreditsSetting = "Trade.FuelReserveCredits";

    /// <inheritdoc />
    public async Task<TradeContext> ReadAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);
        var systemMarkets = (await markets.GetAllSnapshotsAsync(cancellationToken))
            .Where(market => market.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase));
        var chains = await supplyChain.GetAsync(port, TimeProvider.System.GetUtcNow(), cancellationToken);
        var agent = await agents.GetAsync(cancellationToken);
        var minProfitPerUnit = await settings.GetAsync<int>(MinProfitPerUnitSetting, cancellationToken);
        var fuelReserve = await settings.GetAsync<long>(FuelReserveCreditsSetting, cancellationToken);

        return new TradeContext(
            new TradeMarketMap(systemWaypoints, systemMarkets, chains),
            agent?.Credits ?? 0,
            Math.Max(0, minProfitPerUnit),
            Math.Max(0, fuelReserve));
    }
}

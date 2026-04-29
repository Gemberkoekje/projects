using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles docked ships by refuelling to full at fuel-selling markets before role-specific docked handlers run.
/// </summary>
public sealed class ShipDockedFuelEventHandler(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IDockedCommandAcceptor dockedCommands,
    ILogger<ShipDockedFuelEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 10;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null || !string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (ship.FuelCapacity <= 0 || ship.FuelCurrent >= ship.FuelCapacity)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var waypointSymbol = ship.WaypointSymbol ?? @event.WaypointSymbol;
        if (string.IsNullOrWhiteSpace(waypointSymbol))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var waypoint = await waypoints.FindAsync(waypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var market = await markets.FindSnapshotByWaypointAsync(waypointSymbol, cancellationToken);
        if (!HasFuelTradeGood(market))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        logger.LogInformation("{Handler}: ship {Ship} refueling at {Waypoint} ({Current}/{Capacity}).", nameof(ShipDockedFuelEventHandler), @event.ShipSymbol, waypointSymbol, ship.FuelCurrent, ship.FuelCapacity);
        await dockedCommands.RefuelAsync(@event.ShipSymbol, fromCargo: false, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private static bool HasFuelTradeGood(SpaceTraders.Application.Interfaces.MarketSnapshot? market)
        => market?.TradeGoods.Any(g => g.Symbol.Equals("FUEL", StringComparison.OrdinalIgnoreCase)) == true;
}

using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles ships in orbit that are out of fuel by switching to drift mode and routing them to a fuel market.
/// </summary>
public sealed class ShipInOrbitFuelRecoveryEventHandler(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus,
    ILogger<ShipInOrbitFuelRecoveryEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 10;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null || !string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (ship.FuelCapacity <= 0 || ship.FuelCurrent > 0 || string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(@event.ShipSymbol, "DRIFT"), cancellationToken);
        }

        var targetFuelMarket = await FindFuelMarketInSystemAsync(ship.SystemSymbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetFuelMarket))
        {
            logger.LogWarning("{Handler}: ship {Ship} is out of fuel in {System} and no fuel market is known.", nameof(ShipInOrbitFuelRecoveryEventHandler), @event.ShipSymbol, ship.SystemSymbol);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (string.Equals(ship.WaypointSymbol, targetFuelMarket, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("{Handler}: ship {Ship} is at fuel market {Waypoint}; docking for refuel.", nameof(ShipInOrbitFuelRecoveryEventHandler), @event.ShipSymbol, targetFuelMarket);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} is out of fuel; coasting to fuel market {Waypoint}.", nameof(ShipInOrbitFuelRecoveryEventHandler), @event.ShipSymbol, targetFuelMarket);
        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, targetFuelMarket, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private async Task<string> FindFuelMarketInSystemAsync(string systemSymbol, CancellationToken cancellationToken)
    {
        var systemWaypoints = await waypoints.GetBySystemAsync(systemSymbol, cancellationToken);
        if (systemWaypoints.Count == 0)
        {
            return string.Empty;
        }

        var marketWaypoints = systemWaypoints
            .Where(w => w.HasMarket)
            .Select(w => w.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (marketWaypoints.Count == 0)
        {
            return string.Empty;
        }

        var snapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var knownFuelMarket = snapshots
            .Where(s => s.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
            .Where(s => marketWaypoints.Contains(s.WaypointSymbol))
            .FirstOrDefault(s => s.TradeGoods.Any(g => g.Symbol.Equals("FUEL", StringComparison.OrdinalIgnoreCase)));

        return knownFuelMarket?.WaypointSymbol ?? string.Empty;
    }
}

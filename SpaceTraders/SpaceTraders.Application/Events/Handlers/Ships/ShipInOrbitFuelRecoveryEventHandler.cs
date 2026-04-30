using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles ships in orbit that are out of fuel or critically low on fuel by routing them to a fuel market.
/// Ships with zero fuel are switched to drift mode and navigated to the nearest known fuel market.
/// Ships with critically low fuel (below 15% capacity) are docked at the current waypoint if it sells
/// fuel, or switched to drift mode and directed to the nearest known fuel market.
/// </summary>
public sealed class ShipInOrbitFuelRecoveryEventHandler(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus,
    ILogger<ShipInOrbitFuelRecoveryEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    private const decimal CriticalFuelRatio = 0.15m;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null || !string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        if (ship.FuelCapacity <= 0)
        {
            if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
            {
                await bus.InvokeAsync(new PatchShipNavCommand(@event.ShipSymbol, "DRIFT"), cancellationToken);
            }

            return ChainOfCommandHandlerResult.Skipped();
        }

        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var fuelRatio = (decimal)ship.FuelCurrent / ship.FuelCapacity;
        var isCriticallyLow = fuelRatio <= CriticalFuelRatio;

        if (ship.FuelCurrent > 0 && !isCriticallyLow)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        // Ship is empty or critically low on fuel — route to a fuel market.
        if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(@event.ShipSymbol, "DRIFT"), cancellationToken);
        }

        var targetFuelMarket = await FindFuelMarketInSystemAsync(ship.SystemSymbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetFuelMarket))
        {
            logger.LogWarning(
                "{Handler}: ship {Ship} has critically low fuel in {System} and no fuel market is known.",
                nameof(ShipInOrbitFuelRecoveryEventHandler),
                @event.ShipSymbol,
                ship.SystemSymbol);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (string.Equals(ship.WaypointSymbol, targetFuelMarket, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "{Handler}: ship {Ship} is at fuel market {Waypoint} with critically low fuel; docking for refuel.",
                nameof(ShipInOrbitFuelRecoveryEventHandler),
                @event.ShipSymbol,
                targetFuelMarket);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation(
            "{Handler}: ship {Ship} has critically low fuel ({Current}/{Capacity}); coasting to fuel market {Waypoint}.",
            nameof(ShipInOrbitFuelRecoveryEventHandler),
            @event.ShipSymbol,
            ship.FuelCurrent,
            ship.FuelCapacity,
            targetFuelMarket);
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

using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipTraderDockedEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 200;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Trade", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipTraderDockedEventHandler),
                "DOCKED",
                "UNKNOWN",
                "Trade docked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
        }

        if (!string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var stateMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipTraderDockedEventHandler),
                "DOCKED",
                ship.Status ?? "UNKNOWN",
                "Trade docked handler received ship outside docked state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(stateMismatch);
        }

        var atBuyWaypoint = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        var atSellWaypoint = !string.IsNullOrWhiteSpace(assignment.DestWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        if (atBuyWaypoint && !string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            var unitsToBuy = ship.CargoCapacity - ship.CargoCurrent;
            if (unitsToBuy > 0)
            {
                await dockedCommands.BuyCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, unitsToBuy, cancellationToken);
            }
        }
        else if (atSellWaypoint && !string.IsNullOrWhiteSpace(assignment.CargoSymbol) && ship.CargoCurrent > 0)
        {
            await dockedCommands.SellCargoAsync(@event.ShipSymbol, assignment.CargoSymbol, ship.CargoCurrent, cancellationToken);
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var undockedEvent = new ShipUndockedEvent(
            @event.ShipSymbol,
            ship.SystemSymbol ?? @event.SystemSymbol,
            ship.WaypointSymbol ?? @event.WaypointSymbol,
            @event.CorrelationId,
            @event.EventId,
            now);

        return ChainOfCommandHandlerResult.Handled(undockedEvent);
    }
}

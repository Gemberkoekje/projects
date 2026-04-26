using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public sealed class ShipMinerDockedEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IDockedCommandAcceptor dockedCommands) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 100;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            var missingShipMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipMinerDockedEventHandler),
                "DOCKED",
                "UNKNOWN",
                "Mine docked handler could not load ship state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
        }

        if (!string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            var stateMismatch = new ShipStateMismatchEvent(
                @event.ShipSymbol,
                nameof(ShipMinerDockedEventHandler),
                "DOCKED",
                ship.Status ?? "UNKNOWN",
                "Mine docked handler received ship outside docked state.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(stateMismatch);
        }

        if (ship.CargoInventory is not null)
        {
            foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
            {
                await dockedCommands.SellCargoAsync(@event.ShipSymbol, cargo.Symbol, cargo.Units, cancellationToken);
            }
        }

        if (ship.FuelCapacity > 0 && ship.FuelCurrent < (ship.FuelCapacity / 4))
        {
            await dockedCommands.RefuelAsync(@event.ShipSymbol, cancellationToken);
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

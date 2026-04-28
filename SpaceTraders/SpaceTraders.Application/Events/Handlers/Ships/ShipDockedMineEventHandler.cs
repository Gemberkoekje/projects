using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles mining-role ships that are docked. Sells all cargo, refuels if low, then orbits to continue the mining cycle.
/// </summary>
public sealed class ShipDockedMineEventHandler(
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
            return ChainOfCommandHandlerResult.Skipped();
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
        return ChainOfCommandHandlerResult.Handled();
    }
}

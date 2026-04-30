using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Fallback handler for ShipInOrbitEvent when no role-specific handler matched.
/// When a ship is in orbit but has no active role handler, simply dock it.
/// The docked handlers will then assess the ship's assignment and plan accordingly.
/// </summary>
public sealed class ShipInOrbitEventHandler(
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    ILogger<ShipInOrbitEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);

        if (ship is null)
        {
            logger.LogWarning(
                "{Handler}: Cannot load ship state for {Ship}; ignoring fallback orbit event.",
                nameof(ShipInOrbitEventHandler),
                @event.ShipSymbol);

            return ChainOfCommandHandlerResult.Handled();
        }

        if (!string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "{Handler}: Ship {Ship} not in orbit (status={Status}); ignoring fallback orbit event.",
                nameof(ShipInOrbitEventHandler),
                @event.ShipSymbol,
                ship.Status);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation(
            "{Handler}: Ship {Ship} in orbit with no role-specific handler; docking.",
            nameof(ShipInOrbitEventHandler),
            @event.ShipSymbol);

        await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}

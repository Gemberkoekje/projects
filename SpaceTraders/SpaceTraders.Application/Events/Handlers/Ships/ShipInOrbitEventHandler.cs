using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Fallback handler for ShipInOrbitEvent when no role-specific handler (Mining, Trading, Scouting) matched.
/// Handles general in-orbit events, arrived events, and undocked events that don't have specialized handlers.
/// </summary>
public sealed class ShipInOrbitEventHandler(
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    IMessageBus bus,
    ILogger<ShipInOrbitEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);

        // Handle arrived events - set assignment type to Idle so role planning re-runs
        if (@event is ShipArrivedEvent arrived)
        {
            await bus.InvokeAsync(new AssignShipCommand(
                arrived.ShipSymbol,
                "Idle",
                SystemSymbol: arrived.SystemSymbol,
                WaypointSymbol: arrived.WaypointSymbol,
                CorrelationId: arrived.CorrelationId,
                CausationId: arrived.EventId), cancellationToken);

            return ChainOfCommandHandlerResult.Handled();
        }

        // Handle undocked events - dock and emit idle docked
        if (@event is ShipUndockedEvent undocked)
        {
            if (ship is null)
            {
                var missingShipMismatch = new ShipStateMismatchEvent(
                    undocked.ShipSymbol,
                    nameof(ShipInOrbitEventHandler),
                    "IN_ORBIT",
                    "UNKNOWN",
                    "Fallback undocked handling could not load ship state.",
                    undocked.CorrelationId,
                    undocked.EventId,
                    TimeProvider.System.GetUtcNow());

                return ChainOfCommandHandlerResult.Handled(missingShipMismatch);
            }

            if (string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("{Handler}: no role-specific handler claimed ship {Ship}; docking.", nameof(ShipInOrbitEventHandler), @event.ShipSymbol);
                await inOrbitCommands.DockAsync(undocked.ShipSymbol, cancellationToken);
                var now = TimeProvider.System.GetUtcNow();

                var idleDockedEvent = new ShipIdleDockedEvent(
                    undocked.ShipSymbol,
                    ship.SystemSymbol ?? undocked.SystemSymbol,
                    ship.WaypointSymbol ?? undocked.WaypointSymbol,
                    "Fallback undocked recovery docked ship due to missing or invalid role plan.",
                    undocked.CorrelationId,
                    undocked.EventId,
                    now);

                return ChainOfCommandHandlerResult.Handled(idleDockedEvent);
            }

            var mismatchEvent = new ShipStateMismatchEvent(
                undocked.ShipSymbol,
                nameof(ShipInOrbitEventHandler),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Fallback undocked handler received a ship outside orbit state.",
                undocked.CorrelationId,
                undocked.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(mismatchEvent);
        }

        // Handle general in-orbit events - dock if already docked, otherwise dock
        var systemSymbol = ship?.SystemSymbol ?? @event.SystemSymbol;
        var waypointSymbol = ship?.WaypointSymbol ?? @event.WaypointSymbol;

        if (ship is null || !string.Equals(ship.Status, "DOCKED", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("{Handler}: no role-specific in-orbit handler claimed ship {Ship}; docking.", nameof(ShipInOrbitEventHandler), @event.ShipSymbol);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
        }

        await bus.InvokeAsync(new AssignShipCommand(
            @event.ShipSymbol,
            "Idle",
            SystemSymbol: systemSymbol,
            WaypointSymbol: waypointSymbol,
            CorrelationId: @event.CorrelationId,
            CausationId: @event.EventId), cancellationToken);

        return ChainOfCommandHandlerResult.Handled();
    }
}

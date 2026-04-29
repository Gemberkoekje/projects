using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Terminal fallback handler for <see cref="ShipDockedEvent"/>.
/// If a ship reaches this point in the docked chain, no role-specific handler claimed it,
/// so a fresh assignment cycle is started by setting assignment type to Idle.
/// </summary>
public sealed class ShipDockedEventHandler(
    IMessageBus bus,
    ILogger<ShipDockedEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "{Handler}: ship {Ship} reached terminal docked fallback; assigning Idle.",
            nameof(ShipDockedEventHandler),
            @event.ShipSymbol);

        await bus.InvokeAsync(new AssignShipCommand(
            @event.ShipSymbol,
            "Idle",
            SystemSymbol: @event.SystemSymbol,
            WaypointSymbol: @event.WaypointSymbol,
            CorrelationId: @event.CorrelationId,
            CausationId: @event.EventId), cancellationToken);

        return ChainOfCommandHandlerResult.Handled();
    }
}

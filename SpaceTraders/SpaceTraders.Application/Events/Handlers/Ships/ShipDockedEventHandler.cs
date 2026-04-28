using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Fallback handler for ShipDockedEvent when no role-specific handler (Mining, Trading, Scouting) matched.
/// Handles ShipRefueledEvent and ShipRoleSetEvent inline, then transitions to idle-docked decision chain.
/// </summary>
public sealed class ShipDockedEventHandler(
    IMessageBus bus,
    ILogger<ShipDockedEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public int Priority => 1000;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        // Handle refueled event inline — set assignment type to Idle so role planning re-runs
        if (@event is ShipRefueledEvent)
        {
            logger.LogInformation("{Handler}: ship {Ship} refueled.", nameof(ShipDockedEventHandler), @event.ShipSymbol);
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // Handle API role-set event inline — re-enter idle docked decision chain
        if (@event is ShipRoleSetEvent roleSetEvent)
        {
            logger.LogInformation("{Handler}: ship {Ship} API role set to {Role}.", nameof(ShipDockedEventHandler), @event.ShipSymbol, roleSetEvent.Role);

            var roleSetIdleEvent = new ShipIdleDockedEvent(
                @event.ShipSymbol,
                @event.SystemSymbol,
                @event.WaypointSymbol,
                $"Ship API role set to {roleSetEvent.Role}; entering idle docked decision.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(roleSetIdleEvent);
        }

        // Handle assignment type set — re-enter idle docked decision chain
        if (@event is ShipAssignmentTypeSetEvent assignmentTypeSet)
        {
            logger.LogInformation("{Handler}: ship {Ship} assignment type set to {AssignmentType}; entering idle docked decision.", nameof(ShipDockedEventHandler), @event.ShipSymbol, assignmentTypeSet.AssignmentType);

            var idleDockedEvent = new ShipIdleDockedEvent(
                @event.ShipSymbol,
                @event.SystemSymbol,
                @event.WaypointSymbol,
                $"Assignment type set to {assignmentTypeSet.AssignmentType}; entering idle docked decision.",
                @event.CorrelationId,
                @event.EventId,
                TimeProvider.System.GetUtcNow());

            return ChainOfCommandHandlerResult.Handled(idleDockedEvent);
        }

        // Default fallback for unhandled docked events
        var fallbackIdleDockedEvent = new ShipIdleDockedEvent(
            @event.ShipSymbol,
            @event.SystemSymbol,
            @event.WaypointSymbol,
            "Ship docked; entering idle docked role selection.",
            @event.CorrelationId,
            @event.EventId,
            TimeProvider.System.GetUtcNow());

        return ChainOfCommandHandlerResult.Handled(fallbackIdleDockedEvent);
    }
}

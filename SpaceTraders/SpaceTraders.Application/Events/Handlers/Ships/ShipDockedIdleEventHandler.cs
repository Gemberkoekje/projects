using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Domain.Events.Ships;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles <see cref="ShipDockedEvent"/> for idle ships by triggering the fleet orchestrator
/// so that idle ships receive strategic assignments from <see cref="IFleetOrchestrator"/>
/// rather than being planned locally via <see cref="Services.IShipAssignmentPlanner"/>.
/// Ship planners then execute the active assignment on the next automation tick.
/// </summary>
public sealed class ShipDockedIdleEventHandler(
    IShipAssignmentRepository assignments,
    IFleetOrchestrator orchestrator,
    ILogger<ShipDockedIdleEventHandler> logger) : IChainOfCommandEventHandler<ShipDockedEvent>
{
    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Idle", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        logger.LogInformation(
            "{Handler}: ship {Ship} is idle; triggering fleet orchestrator to assign work.",
            nameof(ShipDockedIdleEventHandler),
            @event.ShipSymbol);

        // Phase 6.5c: delegate assignment to the orchestrator so idle ships receive
        // strategic role assignments instead of locally-planned ones.
        await orchestrator.EvaluateAndAssignAsync(cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }
}

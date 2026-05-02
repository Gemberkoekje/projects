using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="MoveToWaypointGoal"/>.
/// Navigates the ship to the target waypoint from any starting state.
/// </summary>
public sealed class MoveToWaypointGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is MoveToWaypointGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var moveGoal = (MoveToWaypointGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await dockedCommands.OrbitAsync(ship.Symbol, ct);
        }
        else if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        if (string.Equals(ship.WaypointSymbol, moveGoal.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return GoalExecutionResult.Completed("Arrived at target waypoint.");
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
        }
        else if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, moveGoal.TargetWaypointSymbol, ct);
        return GoalExecutionResult.WaitingForArrival($"Navigating to {moveGoal.TargetWaypointSymbol}.");
    }
}

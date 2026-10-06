using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="DeployProbeGoal"/>: one flight of a probe (PLAN.md slice 6.3). The probe flies in
/// CRUISE: it has no tank, so no flight costs it fuel, and DRIFT would only make it ten times slower. Its
/// arrival fetches the market and the shipyard there, and docks it; then the goal ends, and the probe plan
/// gives it the next market, or leaves it where it is.
/// </summary>
/// <remarks>
/// A flight to another system (PLAN.md slice 6.28) jumps through the built gates on the way, as every flight between systems
/// does (<see cref="GoalJumps"/>): the antimatter of each jump is the only thing it costs. When no way is known any more, or
/// a jump would leave less than the credit floor, the goal ends and the probe plan chooses again; a jump the API refuses
/// blocks the goal (<see cref="GoalJumps.RefusedReason"/>), and the probe plan, whose ways leave that gate alone for an hour,
/// chooses again too.
/// </remarks>
public sealed class DeployProbeGoalExecutor(
    IShipGoalRepository goals,
    GoalJumps jumps,
    IMessageBus bus,
    ILogger<DeployProbeGoalExecutor> logger) : IShipGoalExecutor
{
    private const string CruiseMode = "CRUISE";
    private const string DriftMode = "DRIFT";

    /// <inheritdoc />
    public bool CanExecute(ShipGoal goal) => goal is DeployProbeGoal;

    /// <inheritdoc />
    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var flight = (DeployProbeGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Probe is in transit.");
        }

        if (string.Equals(ship.WaypointSymbol, flight.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase))
        {
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);
            return GoalExecutionResult.Completed($"Probe at {flight.TargetWaypointSymbol}.");
        }

        // The old probe plan parked probes in DRIFT, and the navigation's fuel fallback can leave a ship in
        // it (B47).
        if (string.Equals(ship.FlightMode, DriftMode, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("DeployProbeGoalExecutor: probe {ShipSymbol} switches from DRIFT to CRUISE.", ship.Symbol);
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, CruiseMode), ct);
            return GoalExecutionResult.Progressing("Switching the probe to CRUISE.");
        }

        if (!string.Equals(ship.SystemSymbol, WaypointSymbols.SystemOf(flight.TargetWaypointSymbol), StringComparison.OrdinalIgnoreCase))
        {
            return await AbroadAsync(ship, flight, ct);
        }

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, flight.TargetWaypointSymbol), ct);
        return GoalExecutionResult.WaitingForArrival($"Probe flying to {flight.TargetWaypointSymbol}.");
    }

    /// <summary>A step of the flight to a market in another system: to the gate, or the jump from it (<see cref="GoalJumps"/>).</summary>
    private async Task<GoalExecutionResult> AbroadAsync(ShipModel ship, DeployProbeGoal flight, CancellationToken ct)
    {
        var step = await jumps.TowardsAsync(ship, flight.TargetWaypointSymbol, ct);
        switch (step.Outcome)
        {
            case JumpStepOutcome.NoWay:
            case JumpStepOutcome.ShortOfCredits:
                // A goal that waited here would look stuck (ShipStuck): the probe plan chooses again, and it sends no probe
                // where no way is known, or the jumps would leave less than the floor.
                await goals.ClearActiveGoalAsync(ship.Symbol, ct);
                logger.LogDebug(
                    "DeployProbeGoalExecutor: probe {ShipSymbol} stops its flight to {WaypointSymbol} ({Outcome}); the probe plan chooses again.",
                    ship.Symbol,
                    flight.TargetWaypointSymbol,
                    step.Outcome);
                return GoalExecutionResult.Progressing($"{step.Result.Reason} The probe plan chooses again.");

            case JumpStepOutcome.Refused:
                await goals.BlockGoalAsync(ship.Symbol, flight.GoalId, GoalJumps.RefusedReason, ct);
                return step.Result;

            default:
                return step.Result;
        }
    }
}

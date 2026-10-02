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
public sealed class DeployProbeGoalExecutor(
    IShipGoalRepository goals,
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

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, flight.TargetWaypointSymbol), ct);
        return GoalExecutionResult.WaitingForArrival($"Probe flying to {flight.TargetWaypointSymbol}.");
    }
}

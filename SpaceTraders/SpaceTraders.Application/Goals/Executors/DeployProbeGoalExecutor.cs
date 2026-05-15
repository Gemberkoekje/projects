using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Executor for <see cref="DeployProbeGoal"/>.
/// Navigates the probe to its target waypoint using DRIFT mode (for fuel-less ships),
/// then marks the deployment complete so the <see cref="IProbeDeploymentPlanService"/> can advance.
/// </summary>
public sealed class DeployProbeGoalExecutor(
    IShipGoalRepository goals,
    IProbeDeploymentPlanService probeDeploymentPlan,
    IMessageBus bus,
    ILogger<DeployProbeGoalExecutor> logger) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is DeployProbeGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(
        ShipModel ship,
        ShipGoal goal,
        ShipGoalContext ctx,
        CancellationToken ct)
    {
        var deployGoal = (DeployProbeGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Probe is in transit to deployment target.");
        }

        var atTarget = string.Equals(
            ship.WaypointSymbol,
            deployGoal.TargetWaypointSymbol,
            StringComparison.OrdinalIgnoreCase);

        if (atTarget)
        {
            // Ensure the probe is docked before setting DRIFT mode.
            if (ship.LocalStatus == ShipLocalStatus.InOrbit)
            {
                await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, deployGoal.TargetWaypointSymbol), ct);
                return GoalExecutionResult.Progressing("Docking probe at deployment target.");
            }

            // Probe is docked at target — set DRIFT flight mode if not already set.
            if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "DeployProbeGoalExecutor: setting DRIFT mode for probe {Symbol} at {Waypoint}.",
                    ship.Symbol,
                    deployGoal.TargetWaypointSymbol);

                await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
            }

            logger.LogInformation(
                "DeployProbeGoalExecutor: probe {Symbol} deployed at {Waypoint}; advancing deployment plan.",
                ship.Symbol,
                deployGoal.TargetWaypointSymbol);

            await probeDeploymentPlan.AdvanceAsync(deployGoal.TargetWaypointSymbol, ct);
            await goals.ClearActiveGoalAsync(ship.Symbol, ct);

            return GoalExecutionResult.Completed($"Probe deployed at {deployGoal.TargetWaypointSymbol} in DRIFT mode.");
        }

        // Not yet at target — set DRIFT mode before navigating if the probe has no fuel capacity.
        if (ship.FuelCapacity <= 0
            && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "DeployProbeGoalExecutor: probe {Symbol} has no fuel; switching to DRIFT mode before navigating.",
                ship.Symbol);

            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
        }

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, deployGoal.TargetWaypointSymbol), ct);
        return GoalExecutionResult.WaitingForArrival($"Probe navigating to deployment target {deployGoal.TargetWaypointSymbol}.");
    }
}

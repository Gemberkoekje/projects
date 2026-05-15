using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Assigns a <see cref="DeployProbeGoal"/> to the named probe and triggers execution.
/// The goal executor handles navigation, DRIFT-mode activation, and plan advancement.
/// </summary>
public sealed record DeployProbeCommand
{
    public required string ProbeSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public DeployProbeCommand(string ProbeSymbol, string DestinationWaypoint)
    {
        this.ProbeSymbol = ProbeSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed class DeployProbeHandler(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipGoalExecutorService goalExecutor,
    ILogger<DeployProbeHandler> logger)
{
    public async Task Handle(DeployProbeCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "DeployProbeHandler: assigning deploy goal to probe {Symbol} → {Destination}.",
            command.ProbeSymbol,
            command.DestinationWaypoint);

        var probe = await ships.FindAsync(command.ProbeSymbol, cancellationToken);
        if (probe is null)
        {
            logger.LogWarning("DeployProbeHandler: probe {Symbol} not found.", command.ProbeSymbol);
            return;
        }

        var existingGoal = await goals.GetActiveGoalAsync(command.ProbeSymbol, cancellationToken);
        if (existingGoal is DeployProbeGoal existing
            && string.Equals(existing.TargetWaypointSymbol, command.DestinationWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "DeployProbeHandler: probe {Symbol} already has deploy goal for {Destination}; skipping re-assignment.",
                command.ProbeSymbol,
                command.DestinationWaypoint);
            return;
        }

        var deployGoal = new DeployProbeGoal
        {
            TargetWaypointSymbol = command.DestinationWaypoint,
        };

        await goals.SetActiveGoalAsync(command.ProbeSymbol, deployGoal, cancellationToken);

        logger.LogInformation(
            "DeployProbeHandler: probe {Symbol} goal set; executing first step toward {Destination}.",
            command.ProbeSymbol,
            command.DestinationWaypoint);

        await goalExecutor.ExecuteAsync(command.ProbeSymbol, cancellationToken);
    }
}

using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

/// <summary>
/// Phase 11a: routes a fleet-level goal to the appropriate ship goal via the assignment resolver,
/// then activates goal execution by setting the active goal and publishing a
/// <see cref="ShipAutomationTickEvent"/>.
/// </summary>
public sealed record AssignShipToGoalCommand
{
    public required string ShipSymbol { get; init; }

    public required FleetGoal Goal { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AssignShipToGoalCommand(string ShipSymbol, FleetGoal Goal)
    {
        this.ShipSymbol = ShipSymbol;
        this.Goal = Goal;
    }
}

/// <summary>
/// Phase 11a: handles <see cref="AssignShipToGoalCommand"/> by dispatching to the appropriate
/// resolver path based on the <see cref="FleetGoalKind"/>, persisting the resolved
/// <see cref="ShipGoal"/>, and triggering first-step execution.
/// </summary>
public sealed class AssignShipToGoalCommandHandler(
    IShipRepository ships,
    IShipGoalRepository goalRepository,
    IAssignmentResolver assignmentResolver,
    ISettingsRepository settings,
    IShipyardRepository shipyards,
    IMessageBus bus,
    ILogger<AssignShipToGoalCommandHandler> logger)
{
    public async Task Handle(AssignShipToGoalCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "AssignShipToGoalCommandHandler: ship={Ship} goalKind={Kind}",
            command.ShipSymbol,
            command.Goal.Kind);

        switch (command.Goal.Kind)
        {
            case FleetGoalKind.Contract:
            case FleetGoalKind.Construction:
                await HandleResourceProductionAsync(command, cancellationToken);
                break;

            case FleetGoalKind.MarketCoverage:
                await HandleMarketCoverageAsync(command, cancellationToken);
                break;

            case FleetGoalKind.FleetExpansion:
                await HandleFleetExpansionAsync(command, cancellationToken);
                break;

            default:
                logger.LogWarning(
                    "AssignShipToGoalCommandHandler: unrecognised FleetGoalKind {Kind} for ship {Ship}; ignoring.",
                    command.Goal.Kind,
                    command.ShipSymbol);
                break;
        }
    }

    private async Task HandleResourceProductionAsync(
        AssignShipToGoalCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Goal.TradeSymbol))
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: {Kind} goal for ship {Ship} has no TradeSymbol; skipping.",
                command.Goal.Kind,
                command.ShipSymbol);
            return;
        }

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: ship {Ship} not found; cannot resolve resource production goal.",
                command.ShipSymbol);
            return;
        }

        var purpose = command.Goal.Kind == FleetGoalKind.Contract
            ? ResourceProductionPurpose.Contract
            : ResourceProductionPurpose.Construction;

        var purposeId = command.Goal.Kind == FleetGoalKind.Contract
            ? command.Goal.ContractId
            : command.Goal.DestinationWaypoint;

        var productionGoal = new ResourceProductionGoal(
            command.Goal.TradeSymbol,
            command.Goal.RemainingUnits,
            purpose,
            command.Goal.Priority,
            PurposeId: purposeId,
            Deadline: command.Goal.Deadline);

        var shipGoal = await assignmentResolver.ResolveAsync(ship, productionGoal, cancellationToken);
        if (shipGoal is null)
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: assignment resolver returned null for ship {Ship} trade {Trade}; goal not set.",
                command.ShipSymbol,
                command.Goal.TradeSymbol);
            return;
        }

        await ActivateGoalAsync(command.ShipSymbol, shipGoal, cancellationToken);
    }

    private async Task HandleMarketCoverageAsync(
        AssignShipToGoalCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Goal.OriginWaypoint))
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: MarketCoverage goal for ship {Ship} has no OriginWaypoint; skipping.",
                command.ShipSymbol);
            return;
        }

        var shipGoal = await assignmentResolver.ResolveMarketCoverageAsync(
            command.Goal.OriginWaypoint,
            cancellationToken);

        await ActivateGoalAsync(command.ShipSymbol, shipGoal, cancellationToken);
    }

    private async Task HandleFleetExpansionAsync(
        AssignShipToGoalCommand command,
        CancellationToken cancellationToken)
    {
        var shipType = command.Goal.ExpansionShipType;
        var shipyardWaypoint = command.Goal.ExpansionShipyardWaypoint;

        // Fall back to settings if the evaluator did not supply explicit expansion info.
        if (string.IsNullOrWhiteSpace(shipType))
        {
            shipType = await settings.GetAsync<string>("FleetExpansion.PreferredShipType", cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(shipType))
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: FleetExpansion goal has no preferred ship type configured; skipping.");
            return;
        }

        if (string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            shipyardWaypoint = await shipyards.FindShipyardForTypeAsync(shipType, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            logger.LogWarning(
                "AssignShipToGoalCommandHandler: no known shipyard for type {ShipType}; cannot expand fleet.",
                shipType);
            return;
        }

        logger.LogInformation(
            "AssignShipToGoalCommandHandler: FleetExpansion — purchasing {ShipType} at {Shipyard}.",
            shipType,
            shipyardWaypoint);

        await bus.SendAsync(new PurchaseShipCommand(
            shipType,
            shipyardWaypoint,
            TargetGoal: command.Goal.ExpansionTargetGoal));
    }

    private async Task ActivateGoalAsync(
        string shipSymbol,
        ShipGoal shipGoal,
        CancellationToken cancellationToken)
    {
        await goalRepository.SetActiveGoalAsync(shipSymbol, shipGoal, cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        await bus.PublishAsync(new ShipAutomationTickEvent(
            shipSymbol,
            "GoalAssigned",
            now,
            Guid.NewGuid(),
            Guid.Empty));

        logger.LogInformation(
            "AssignShipToGoalCommandHandler: ship {Ship} active goal set to {GoalKind} (id={GoalId}).",
            shipSymbol,
            shipGoal.Kind,
            shipGoal.GoalId);
    }
}

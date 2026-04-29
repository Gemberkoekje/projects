using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles mining-role ships in orbit based on the ship's current position and cargo state.
/// At the mining origin with space in cargo: extract resources.
/// Has cargo: navigate to the sell destination or dock if already there.
/// No cargo: navigate to the mining origin or dock if already there.
/// </summary>
public sealed class ShipInOrbitMineEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    ISurveyRepository surveys,
    IInOrbitCommandAcceptor inOrbitCommands,
    INavigationPlanningService navigationPlanning,
    IMessageBus bus,
    ILogger<ShipInOrbitMineEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 60;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null ||
            (!assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase) &&
             !assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase)))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: @event.SystemSymbol,
                WaypointSymbol: @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (!string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && ship.WaypointSymbol?.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase) == true;

        // At mining origin and cargo not full: extract
        if (atOrigin && ship.CargoCurrent < ship.CargoCapacity)
        {
            if (assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("{Handler}: ship {Ship} siphoning at {Waypoint}.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, ship.WaypointSymbol);
                await inOrbitCommands.SiphonAsync(@event.ShipSymbol, cancellationToken);
                return ChainOfCommandHandlerResult.Handled();
            }

            var activeSurveys = await surveys.GetActiveByWaypointAsync(ship.WaypointSymbol ?? string.Empty, cancellationToken);
            if (ship.HasSurveyEquipment && activeSurveys.Count == 0)
            {
                logger.LogInformation("{Handler}: ship {Ship} surveying before extraction at {Waypoint}.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, ship.WaypointSymbol);
                await inOrbitCommands.SurveyAsync(@event.ShipSymbol, cancellationToken);
                return ChainOfCommandHandlerResult.Handled();
            }

            logger.LogInformation("{Handler}: ship {Ship} extracting at {Waypoint}.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, ship.WaypointSymbol);
            await inOrbitCommands.ExtractAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // Has cargo: head to sell destination
        if (ship.CargoCurrent > 0)
        {
            if (string.IsNullOrWhiteSpace(assignment.DestWaypoint))
            {
                await bus.InvokeAsync(new AssignShipCommand(
                    @event.ShipSymbol,
                    "Idle",
                    SystemSymbol: ship.SystemSymbol ?? @event.SystemSymbol,
                    WaypointSymbol: ship.WaypointSymbol ?? @event.WaypointSymbol,
                    CorrelationId: @event.CorrelationId,
                    CausationId: @event.EventId), cancellationToken);
                return ChainOfCommandHandlerResult.Handled();
            }

            var atSellDest = ship.WaypointSymbol?.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase) == true;
            if (atSellDest)
            {
                logger.LogInformation("{Handler}: ship {Ship} at sell destination {Waypoint}; docking.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, ship.WaypointSymbol);
                await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
                return ChainOfCommandHandlerResult.Handled();
            }

            logger.LogInformation("{Handler}: ship {Ship} navigating to sell destination {Waypoint}.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, assignment.DestWaypoint);
            await ApplyFlightModeAsync(@event.ShipSymbol, ship, assignment.DestWaypoint, bus, cancellationToken);
            await inOrbitCommands.NavigateAsync(@event.ShipSymbol, assignment.DestWaypoint, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        // No cargo: return to mining origin
        if (string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            await bus.InvokeAsync(new AssignShipCommand(
                @event.ShipSymbol,
                "Idle",
                SystemSymbol: ship.SystemSymbol ?? @event.SystemSymbol,
                WaypointSymbol: ship.WaypointSymbol ?? @event.WaypointSymbol,
                CorrelationId: @event.CorrelationId,
                CausationId: @event.EventId), cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (atOrigin)
        {
            logger.LogInformation("{Handler}: ship {Ship} at origin {Waypoint} with empty cargo; docking.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, ship.WaypointSymbol);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} navigating to origin {Waypoint}.", nameof(ShipInOrbitMineEventHandler), @event.ShipSymbol, assignment.OriginWaypoint);
        await ApplyFlightModeAsync(@event.ShipSymbol, ship, assignment.OriginWaypoint, bus, cancellationToken);
        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, assignment.OriginWaypoint, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private async Task ApplyFlightModeAsync(
        string shipSymbol,
        SpaceTraders.Application.Ports.ShipModel ship,
        string destinationWaypoint,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (!ShouldAdjustFlightMode(ship, destinationWaypoint))
        {
            return;
        }

        var plan = await navigationPlanning.BuildPlanAsync(ship, destinationWaypoint, cancellationToken);
        if (!string.IsNullOrWhiteSpace(plan.RecommendedFlightMode) &&
            !string.Equals(ship.FlightMode, plan.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(shipSymbol, plan.RecommendedFlightMode), cancellationToken);
        }
    }

    private static bool ShouldAdjustFlightMode(
        SpaceTraders.Application.Ports.ShipModel ship,
        string destinationWaypoint)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol) || string.IsNullOrWhiteSpace(destinationWaypoint))
        {
            return false;
        }

        var destinationSystem = ExtractSystemSymbol(destinationWaypoint);
        return ship.SystemSymbol.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}

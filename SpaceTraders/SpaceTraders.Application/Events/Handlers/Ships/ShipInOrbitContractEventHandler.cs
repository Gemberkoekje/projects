using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles contract-role ships while in orbit.
/// Event handlers must emit only commands, never events directly.
/// </summary>
public sealed class ShipInOrbitContractEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IInOrbitCommandAcceptor inOrbitCommands,
    INavigationPlanningService navigationPlanning,
    IMessageBus bus,
    ILogger<ShipInOrbitContractEventHandler> logger) : IChainOfCommandEventHandler<ShipInOrbitEvent>
{
    public int Priority => 65;

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipInOrbitEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null || !assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            logger.LogWarning(
                "{Handler}: Could not load ship state for {Ship}. Cannot handle contract event.",
                nameof(ShipInOrbitContractEventHandler),
                @event.ShipSymbol);

            return ChainOfCommandHandlerResult.Failed(
                $"Contract in-orbit handler could not load ship state for {@event.ShipSymbol}.");
        }

        if (!string.Equals(ship.Status, "IN_ORBIT", StringComparison.OrdinalIgnoreCase))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var destination = ResolveDestination(assignment, ship);
        if (string.IsNullOrWhiteSpace(destination))
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

        if (ship.WaypointSymbol?.Equals(destination, StringComparison.OrdinalIgnoreCase) == true)
        {
            logger.LogInformation("{Handler}: ship {Ship} docking at {Waypoint} for contract assignment.", nameof(ShipInOrbitContractEventHandler), @event.ShipSymbol, destination);
            await inOrbitCommands.DockAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        logger.LogInformation("{Handler}: ship {Ship} navigating to {Waypoint} for contract assignment.", nameof(ShipInOrbitContractEventHandler), @event.ShipSymbol, destination);
        await ApplyFlightModeAsync(@event.ShipSymbol, ship, destination, bus, cancellationToken);
        await inOrbitCommands.NavigateAsync(@event.ShipSymbol, destination, cancellationToken);
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

    private static string ResolveDestination(
        SpaceTraders.Application.DTOs.ShipAssignmentDto assignment,
        SpaceTraders.Application.Ports.ShipModel ship)
    {
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol ?? string.Empty, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (cargoUnits > 0 && !string.IsNullOrWhiteSpace(assignment.DestWaypoint))
        {
            return assignment.DestWaypoint;
        }

        if (!string.IsNullOrWhiteSpace(assignment.OriginWaypoint))
        {
            return assignment.OriginWaypoint;
        }

        return assignment.DestWaypoint ?? string.Empty;
    }
}

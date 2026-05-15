using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.ValueObjects;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// Top-level command that navigates a ship to a destination waypoint.
/// Orchestrates the full pre-flight sequence (refuel → orbit → navigate) using subcommands.
/// After the ship arrives, <see cref="NavigateToWaypointArrivedCommand"/> handles
/// the post-arrival sequence (update market/shipyard → dock → emit completed).
/// </summary>
public sealed record NavigateToWaypointCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigateToWaypointCommand(string ShipSymbol, string DestinationWaypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

/// <summary>
/// Internal command published by <see cref="ShipArrivedEvent"/> handling when the ship
/// was navigating via <see cref="NavigateToWaypointCommand"/>.
/// Handles the post-arrival sequence: update market/shipyard data, dock, emit completed.
/// </summary>
public sealed record NavigateToWaypointArrivedCommand
{
    public required string ShipSymbol { get; init; }

    public required string DestinationWaypoint { get; init; }

    public required Guid GoalId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NavigateToWaypointArrivedCommand(string ShipSymbol, string DestinationWaypoint, Guid GoalId)
    {
        this.ShipSymbol = ShipSymbol;
        this.DestinationWaypoint = DestinationWaypoint;
        this.GoalId = GoalId;
    }
}

/// <summary>
/// Handles <see cref="NavigateToWaypointCommand"/>: the pre-flight phase.
/// Steps:
/// 1. Check if ship is already at destination — emit completed and return.
/// 2. If docked: refuel (if at fuel market), then orbit.
/// 3. If in orbit: navigate.
/// 4. If neither: publish mismatch and return.
/// </summary>
public sealed class NavigateToWaypointHandler(
    IShipRepository ships,
    IShipGoalRepository goals,
    IWaypointRepository waypoints,
    IOrbitSubCommand orbit,
    INavigateSubCommand navigate,
    IRefuelSubCommand refuel,
    IMessageBus bus,
    ILogger<NavigateToWaypointHandler> logger)
{
    public async Task Handle(NavigateToWaypointCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "NavigateToWaypointHandler: ship {Symbol} → {Destination}.",
            command.ShipSymbol,
            command.DestinationWaypoint);

        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var status = ship?.LocalStatus ?? ShipLocalStatus.None;

        // Step 1: already at destination.
        if (string.Equals(ship?.WaypointSymbol, command.DestinationWaypoint, StringComparison.OrdinalIgnoreCase)
            && status != ShipLocalStatus.InTransit)
        {
            logger.LogInformation(
                "NavigateToWaypointHandler: ship {Symbol} is already at {Destination}; emitting completed.",
                command.ShipSymbol,
                command.DestinationWaypoint);

            var activeGoal = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
            await bus.PublishAsync(new ShipNavigationCompletedEvent(
                command.ShipSymbol,
                command.DestinationWaypoint,
                activeGoal?.GoalId ?? Guid.Empty));
            return;
        }

        // Step 2: if docked, refuel then orbit.
        if (status == ShipLocalStatus.Docked)
        {
            var waypoint = await waypoints.FindAsync(ship!.WaypointSymbol ?? string.Empty, cancellationToken);
            if (waypoint?.HasMarket == true && ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity)
            {
                await refuel.ExecuteAsync(command.ShipSymbol, fromCargo: false, cancellationToken);
            }

            await orbit.ExecuteAsync(command.ShipSymbol, cancellationToken);

            // Refresh ship state after orbit.
            ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
            status = ship?.LocalStatus ?? ShipLocalStatus.None;
        }

        // Step 3: must now be in orbit; navigate to destination.
        if (status != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(NavigateToWaypointCommand),
                "IN_ORBIT",
                ship?.Status ?? "UNKNOWN",
                "Ship must be in orbit before navigation.");

            logger.LogWarning(
                "NavigateToWaypointHandler: ship {Symbol} is not in orbit after orbit step; status={Status}.",
                command.ShipSymbol,
                ship?.Status ?? "UNKNOWN");
            return;
        }

        var activeGoalForNav = await goals.GetActiveGoalAsync(command.ShipSymbol, cancellationToken);
        await navigate.ExecuteAsync(
            command.ShipSymbol,
            command.DestinationWaypoint,
            activeGoalForNav?.GoalId ?? Guid.Empty,
            cancellationToken);
    }
}

/// <summary>
/// Handles <see cref="NavigateToWaypointArrivedCommand"/>: the post-arrival phase.
/// Steps:
/// 5. Update market and shipyard data at the arrived waypoint (if available).
/// 6. Dock the ship.
/// 7. Emit <see cref="ShipNavigationCompletedEvent"/> so the next command can be ordered.
/// </summary>
public sealed class NavigateToWaypointArrivedHandler(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IShipyardRepository shipyards,
    IDockSubCommand dock,
    Ports.ISpaceTradersPort port,
    IMessageBus bus,
    ILogger<NavigateToWaypointArrivedHandler> logger)
{
    public async Task Handle(NavigateToWaypointArrivedCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "NavigateToWaypointArrivedHandler: ship {Symbol} arrived at {Destination}.",
            command.ShipSymbol,
            command.DestinationWaypoint);

        // Step 5: refresh market and shipyard data at destination.
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        var systemSymbol = ship?.SystemSymbol ?? string.Empty;

        var waypoint = await waypoints.FindAsync(command.DestinationWaypoint, cancellationToken);

        if (waypoint?.HasMarket == true && !string.IsNullOrWhiteSpace(systemSymbol))
        {
            try
            {
                var market = await port.GetMarketAsync(systemSymbol, command.DestinationWaypoint, cancellationToken);
                await markets.UpsertAsync(market, cancellationToken);
                await bus.PublishAsync(new MarketDataRefreshedEvent(
                    new WaypointSymbol(command.DestinationWaypoint),
                    market.TradeGoodsJson));
                logger.LogInformation(
                    "NavigateToWaypointArrivedHandler: market data updated for {Waypoint}.",
                    command.DestinationWaypoint);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NavigateToWaypointArrivedHandler: failed to update market data for {Waypoint}.", command.DestinationWaypoint);
            }
        }

        if (waypoint?.HasShipyard == true && !string.IsNullOrWhiteSpace(systemSymbol))
        {
            try
            {
                var shipyard = await port.GetShipyardAsync(systemSymbol, command.DestinationWaypoint, cancellationToken);
                await shipyards.UpsertAsync(shipyard, cancellationToken);
                logger.LogInformation(
                    "NavigateToWaypointArrivedHandler: shipyard data updated for {Waypoint}.",
                    command.DestinationWaypoint);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "NavigateToWaypointArrivedHandler: failed to update shipyard data for {Waypoint}.", command.DestinationWaypoint);
            }
        }

        // Step 6: dock.
        await dock.ExecuteAsync(command.ShipSymbol, cancellationToken);

        // Step 7: emit navigation completed.
        await bus.PublishAsync(new ShipNavigationCompletedEvent(
            command.ShipSymbol,
            command.DestinationWaypoint,
            command.GoalId));

        logger.LogInformation(
            "NavigateToWaypointArrivedHandler: ship {Symbol} docked at {Destination}; navigation complete.",
            command.ShipSymbol,
            command.DestinationWaypoint);
    }
}

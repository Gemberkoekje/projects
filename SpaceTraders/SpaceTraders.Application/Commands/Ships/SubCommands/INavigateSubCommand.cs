using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships.SubCommands;

/// <summary>
/// Subcommand that navigates an in-orbit ship to a destination waypoint.
/// Calls the SpaceTraders API, persists nav state, publishes <see cref="ShipInTransitEvent"/>,
/// and schedules the <see cref="ShipArrivedEvent"/> via <see cref="IShipEventScheduler"/>.
/// Must only be called when the ship is in orbit.
/// </summary>
public interface INavigateSubCommand
{
    Task ExecuteAsync(string shipSymbol, string destinationWaypoint, Guid goalId, CancellationToken cancellationToken);
}

public sealed class NavigateSubCommand(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IShipEventScheduler scheduler,
    IDashboardNotifier dashboardNotifier,
    IMessageBus bus,
    ILogger<NavigateSubCommand> logger) : INavigateSubCommand
{
    public async Task ExecuteAsync(string shipSymbol, string destinationWaypoint, Guid goalId, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "NavigateSubCommand: navigating ship {Symbol} to {Destination}.",
            shipSymbol,
            destinationWaypoint);

        var now = TimeProvider.System.GetUtcNow();
        var ship = await ships.FindAsync(shipSymbol, cancellationToken);

        var navigation = await NavigateWithFuelFallbackAsync(
            shipSymbol,
            destinationWaypoint,
            ship,
            cancellationToken);

        if (navigation is null)
        {
            logger.LogWarning(
                "NavigateSubCommand: no reachable navigation leg found for ship {Symbol} toward {Destination}; skipping navigate call for this tick.",
                shipSymbol,
                destinationWaypoint);
            return;
        }

        var (result, actualDestination) = navigation.Value;

        await ships.UpdateNavAsync(shipSymbol, result.Nav, result.Fuel, cancellationToken);

        var arrivalTime = result.Nav.ArrivesAt ?? now;

        await bus.PublishAsync(new ShipInTransitEvent(
            shipSymbol,
            ship?.WaypointSymbol ?? result.Nav.WaypointSymbol,
            actualDestination,
            arrivalTime,
            Guid.Empty,
            Guid.Empty,
            now));

        dashboardNotifier.Notify("ships", shipSymbol);
        dashboardNotifier.Notify("fleet-activity", shipSymbol);
        dashboardNotifier.Notify("activity", shipSymbol);

        await scheduler.ScheduleArrivalAsync(shipSymbol, goalId, arrivalTime, cancellationToken);

        logger.LogInformation(
            "NavigateSubCommand: ship {Symbol} in transit to {Destination}, arrives at {Arrival}.",
            shipSymbol,
            actualDestination,
            arrivalTime);
    }

    private async Task<(NavigateActionResult Result, string ActualDestination)?> NavigateWithFuelFallbackAsync(
        string shipSymbol,
        string destinationWaypoint,
        ShipModel? ship,
        CancellationToken cancellationToken)
    {
        try
        {
            var direct = await port.NavigateShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
            return (direct, destinationWaypoint);
        }
        catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
        {
            logger.LogInformation(
                "NavigateSubCommand: insufficient fuel for direct navigation of ship {Symbol} to {Destination}; attempting fallback routing.",
                shipSymbol,
                destinationWaypoint);
        }

        ship = await TrySwitchToDriftForFuelEfficiencyAsync(shipSymbol, ship, cancellationToken);

        try
        {
            var directAfterDrift = await port.NavigateShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
            return (directAfterDrift, destinationWaypoint);
        }
        catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
        {
            logger.LogInformation(
                "NavigateSubCommand: ship {Symbol} still lacks fuel for {Destination} after DRIFT fallback; trying intermediate markets.",
                shipSymbol,
                destinationWaypoint);
        }

        var candidates = await GetIntermediateFuelMarketCandidatesAsync(ship, destinationWaypoint, cancellationToken);

        foreach (var market in candidates)
        {
            try
            {
                var reroute = await port.NavigateShipAsync(shipSymbol, market, cancellationToken);
                logger.LogInformation(
                    "NavigateSubCommand: rerouting ship {Symbol} to intermediate market {FuelMarket} before {Destination}.",
                    shipSymbol,
                    market,
                    destinationWaypoint);
                return (reroute, market);
            }
            catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
            {
                logger.LogDebug(
                    "NavigateSubCommand: intermediate market {FuelMarket} is not reachable for ship {Symbol}; trying next candidate.",
                    market,
                    shipSymbol);
            }
        }

        return null;
    }

    private async Task<ShipModel?> TrySwitchToDriftForFuelEfficiencyAsync(
        string shipSymbol,
        ShipModel? ship,
        CancellationToken cancellationToken)
    {
        if (ship is null || string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            return ship;
        }

        try
        {
            var nav = await port.PatchShipNavAsync(shipSymbol, "DRIFT", cancellationToken);
            await ships.UpdateNavAsync(shipSymbol, nav, null, cancellationToken);
            return await ships.FindAsync(shipSymbol, cancellationToken) ?? ship;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "NavigateSubCommand: unable to switch ship {Symbol} to DRIFT for fuel fallback.", shipSymbol);
            return ship;
        }
    }

    private async Task<IReadOnlyList<string>> GetIntermediateFuelMarketCandidatesAsync(
        ShipModel? ship,
        string destinationWaypoint,
        CancellationToken cancellationToken)
    {
        if (ship is null
            || string.IsNullOrWhiteSpace(ship.SystemSymbol)
            || string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            || ship.WaypointSymbol.Equals(destinationWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var destinationSystem = ExtractSystemSymbol(destinationWaypoint);
        if (!ship.SystemSymbol.Equals(destinationSystem, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
        var current = systemWaypoints.FirstOrDefault(w => w.Symbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));
        var destination = systemWaypoints.FirstOrDefault(w => w.Symbol.Equals(destinationWaypoint, StringComparison.OrdinalIgnoreCase));

        if (current is null || destination is null)
        {
            return [];
        }

        return systemWaypoints
            .Where(w => w.HasMarket
                && !w.Symbol.Equals(current.Symbol, StringComparison.OrdinalIgnoreCase)
                && !w.Symbol.Equals(destination.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => Distance(current, w))
            .ThenBy(w => Distance(w, destination))
            .ThenBy(w => w.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(w => w.Symbol)
            .ToList();
    }

    private static decimal Distance(WaypointCacheModel from, WaypointCacheModel to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return (decimal)Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }

    private static bool IsInsufficientFuelNavigationError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("requires", StringComparison.OrdinalIgnoreCase)
            && message.Contains("fuel", StringComparison.OrdinalIgnoreCase)
            && message.Contains("navigation", StringComparison.OrdinalIgnoreCase);
    }
}

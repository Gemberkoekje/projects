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
        logger.LogDebug(
            "NavigateSubCommand: navigating ship {ShipSymbol} to {Destination}.",
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
                "NavigateSubCommand: no reachable navigation leg found for ship {ShipSymbol} toward {Destination}; skipping navigate call for this tick.",
                shipSymbol,
                destinationWaypoint);
            return;
        }

        var (result, actualDestination) = navigation.Value;

        await ships.UpdateNavAsync(shipSymbol, result.Nav, result.Fuel, cancellationToken);

        var arrivalTime = result.Nav.ArrivesAt ?? now;
        var origin = ship?.WaypointSymbol ?? result.Nav.WaypointSymbol;

        await bus.PublishAsync(new ShipInTransitEvent(
            shipSymbol,
            origin,
            actualDestination,
            arrivalTime,
            Guid.Empty,
            Guid.Empty,
            now));

        dashboardNotifier.Notify("ships", shipSymbol);
        dashboardNotifier.Notify("fleet-activity", shipSymbol);
        dashboardNotifier.Notify("activity", shipSymbol);

        await scheduler.ScheduleArrivalAsync(shipSymbol, goalId, arrivalTime, cancellationToken);

        // A flight logs one line at Information when it leaves (this one) and one when it lands
        // (ShipNavigationCompletedHandler), besides its refuel; the steps between log at Debug (B53).
        logger.LogInformation(
            "NavigateSubCommand: ship {ShipSymbol} in transit from {Origin} to {Destination}, arrives at {Arrival}.",
            shipSymbol,
            origin,
            actualDestination,
            arrivalTime);
    }

    private async Task<(NavigateActionResult Result, string ActualDestination)?> NavigateWithFuelFallbackAsync(
        string shipSymbol,
        string destinationWaypoint,
        ShipModel? ship,
        CancellationToken cancellationToken)
    {
        // A flight the fuel aboard can't pay for isn't asked of the API, which would refuse it with a 400 (B62). Now that the
        // flights plan their refuelling stops (B47), the fallback below is a last resort, so it warns.
        var needed = await FuelNeededAsync(ship, destinationWaypoint, cancellationToken);
        if (ship is not null && needed > ship.FuelCurrent)
        {
            logger.LogWarning(
                "NavigateSubCommand: ship {ShipSymbol} has {Fuel} fuel, short of the {FuelNeeded} a flight to {Destination} in {FlightMode} takes; attempting fallback routing without asking the API.",
                shipSymbol,
                ship.FuelCurrent,
                needed,
                destinationWaypoint,
                ship.FlightMode);
        }
        else
        {
            try
            {
                var direct = await port.NavigateShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
                return (direct, destinationWaypoint);
            }
            catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
            {
                logger.LogWarning(
                    ex,
                    "NavigateSubCommand: insufficient fuel for direct navigation of ship {ShipSymbol} to {Destination}; attempting fallback routing.",
                    shipSymbol,
                    destinationWaypoint);
            }
        }

        // A leg planned in BURN that the fuel aboard no longer pays for flies in CRUISE before anything drifts (D84).
        if (ship is not null && string.Equals(ship.FlightMode, "BURN", StringComparison.OrdinalIgnoreCase))
        {
            ship = await TrySwitchModeAsync(shipSymbol, ship, "CRUISE", cancellationToken);
            if (ship is not null && await FuelNeededAsync(ship, destinationWaypoint, cancellationToken) <= ship.FuelCurrent)
            {
                try
                {
                    var cruised = await port.NavigateShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
                    return (cruised, destinationWaypoint);
                }
                catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
                {
                    logger.LogWarning(
                        ex,
                        "NavigateSubCommand: ship {ShipSymbol} lacks fuel for {Destination} in CRUISE too; attempting fallback routing.",
                        shipSymbol,
                        destinationWaypoint);
                }
            }
        }

        ship = await TrySwitchModeAsync(shipSymbol, ship, "DRIFT", cancellationToken);

        if (ship is null || await FuelNeededAsync(ship, destinationWaypoint, cancellationToken) <= ship.FuelCurrent)
        {
            try
            {
                var directAfterDrift = await port.NavigateShipAsync(shipSymbol, destinationWaypoint, cancellationToken);
                return (directAfterDrift, destinationWaypoint);
            }
            catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
            {
                logger.LogInformation(
                    ex,
                    "NavigateSubCommand: ship {ShipSymbol} still lacks fuel for {Destination} after DRIFT fallback; trying intermediate markets.",
                    shipSymbol,
                    destinationWaypoint);
            }
        }

        var candidates = await GetIntermediateFuelMarketCandidatesAsync(ship, destinationWaypoint, cancellationToken);

        foreach (var market in candidates)
        {
            if (ship is not null && await FuelNeededAsync(ship, market, cancellationToken) > ship.FuelCurrent)
            {
                continue;
            }

            try
            {
                var reroute = await port.NavigateShipAsync(shipSymbol, market, cancellationToken);
                logger.LogInformation(
                    "NavigateSubCommand: rerouting ship {ShipSymbol} to intermediate market {FuelMarket} before {Destination}.",
                    shipSymbol,
                    market,
                    destinationWaypoint);
                return (reroute, market);
            }
            catch (Exception ex) when (IsInsufficientFuelNavigationError(ex.Message))
            {
                logger.LogDebug(
                    ex,
                    "NavigateSubCommand: intermediate market {FuelMarket} is not reachable for ship {ShipSymbol}; trying next candidate.",
                    market,
                    shipSymbol);
            }
        }

        return null;
    }

    /// <summary>
    /// The fuel the ship's flight to <paramref name="destination"/> burns in its flight mode (<see cref="FlightFuel"/>), from
    /// the cached positions: 0 when the ship has no tank (a probe) or a position isn't cached, so nothing holds it back.
    /// </summary>
    private async Task<int> FuelNeededAsync(ShipModel? ship, string destination, CancellationToken cancellationToken)
    {
        if (ship is null || ship.FuelCapacity == 0 || string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return 0;
        }

        var from = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        var to = await waypoints.FindAsync(destination, cancellationToken);
        return from is null || to is null ? 0 : FlightFuel.Needed(ship.FlightMode, (double)Distance(from, to));
    }

    private async Task<ShipModel?> TrySwitchModeAsync(
        string shipSymbol,
        ShipModel? ship,
        string flightMode,
        CancellationToken cancellationToken)
    {
        if (ship is null || string.Equals(ship.FlightMode, flightMode, StringComparison.OrdinalIgnoreCase))
        {
            return ship;
        }

        try
        {
            var nav = await port.PatchShipNavAsync(shipSymbol, flightMode, cancellationToken);
            await ships.UpdateNavAsync(shipSymbol, nav, null, cancellationToken);
            return await ships.FindAsync(shipSymbol, cancellationToken) ?? ship;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "NavigateSubCommand: unable to switch ship {ShipSymbol} to {FlightMode} for fuel fallback.", shipSymbol, flightMode);
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

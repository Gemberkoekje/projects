using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// How the survey and mining executors fly (PLAN.md slice 6.4): in CRUISE, through refuelling stops when
/// the fuel aboard won't reach (<see cref="TradeRoutePlanner"/>), never DRIFT (B47). The navigation
/// carries the goal, so the arrival wakes it (B17), and fills the tank where it leaves a market.
/// </summary>
internal static class GoalFlight
{
    /// <summary>Takes the ship one leg towards a waypoint.</summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="dock">Docks the ship, to refuel before it leaves.</param>
    /// <param name="bus">Sends the navigation.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>The step's outcome.</returns>
    public static async Task<GoalExecutionResult> TowardsAsync(
        TradeMarketMap map,
        ShipModel ship,
        string destination,
        IDockSubCommand dock,
        IMessageBus bus,
        CancellationToken ct)
    {
        var here = ship.WaypointSymbol ?? string.Empty;
        var direct = TradeRoutePlanner.TryPlanFlight(map, ship, destination, out var flight) && flight.Stops.Count == 1;

        // In orbit at a market that sells fuel, without the fuel for the flight: the navigation only
        // fills the tank of a docked ship.
        if (!direct
            && ship.LocalStatus == ShipLocalStatus.InOrbit
            && map.SellsFuel(here)
            && ship.FuelCurrent < ship.FuelCapacity)
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing($"Docking at {here} to refuel before flying to {destination}.");
        }

        var stop = TradeRoutePlanner.NextStop(map, ship, destination);
        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, stop), ct);
        return GoalExecutionResult.WaitingForArrival(
            stop.Equals(destination, StringComparison.OrdinalIgnoreCase)
                ? $"Navigating to {destination}."
                : $"Navigating to {destination}, refuelling at {stop} on the way.");
    }
}

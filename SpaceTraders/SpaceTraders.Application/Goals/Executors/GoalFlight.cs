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
/// carries the goal, so the arrival wakes it (B17), and fills the tank where it leaves a market. A ship
/// left in DRIFT is switched back to CRUISE before it flies (slice 6.10c). The one exception is a trip to
/// a market out of the ship's CRUISE reach, which drifts there (<see cref="DriftAsync"/>, D45).
/// </summary>
internal static class GoalFlight
{
    private const string CruiseMode = "CRUISE";
    private const string DriftMode = "DRIFT";

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
        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, stop) { FlightMode = CruiseMode }, ct);
        return GoalExecutionResult.WaitingForArrival(
            stop.Equals(destination, StringComparison.OrdinalIgnoreCase)
                ? $"Navigating to {destination}."
                : $"Navigating to {destination}, refuelling at {stop} on the way.");
    }

    /// <summary>
    /// Takes the ship to a market out of its CRUISE reach in DRIFT (D45): 1 fuel whatever the distance, about ten times
    /// slower. Its arrival docks it there, and its next flight (<see cref="TowardsAsync"/>) refuels and flies in CRUISE.
    /// </summary>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="market">The market it drifts to: one that sells fuel.</param>
    /// <param name="bus">Sends the navigation.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>The step's outcome.</returns>
    public static async Task<GoalExecutionResult> DriftAsync(ShipModel ship, string market, IMessageBus bus, CancellationToken ct)
    {
        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, market) { FlightMode = DriftMode }, ct);
        return GoalExecutionResult.WaitingForArrival($"Drifting to {market}, out of CRUISE reach.");
    }
}

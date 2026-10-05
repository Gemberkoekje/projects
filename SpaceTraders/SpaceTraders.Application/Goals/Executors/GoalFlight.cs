using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// How the executors fly (PLAN.md slice 6.4, slice 6.19): one leg a step, in the flight mode the route planner gives it
/// (<see cref="TradeRoutePlanner.TryPlanNextLeg"/>, D84): BURN where the fuel allows it and strands nothing, CRUISE
/// otherwise, through refuelling stops when the fuel aboard won't reach (B47); to a waypoint no chain of fuel markets
/// reaches, the fastest way there, which cruises as far as it can and drifts the rest (D45, D84). The navigation carries the
/// goal, so the arrival wakes it (B17), and fills the tank where it leaves a market; a ship left in another mode flies in
/// the one asked for.
/// </summary>
internal static class GoalFlight
{
    /// <summary>Takes the ship one leg towards a waypoint (<see cref="LegTowardsAsync"/>).</summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="dock">Docks the ship, to refuel before it leaves.</param>
    /// <param name="bus">Sends the navigation.</param>
    /// <param name="ct">Stops the work.</param>
    /// <param name="onward">Where the ship must get to from <paramref name="destination"/> next, when the caller knows.</param>
    /// <returns>The step's outcome.</returns>
    public static async Task<GoalExecutionResult> TowardsAsync(
        TradeMarketMap map,
        ShipModel ship,
        string destination,
        IDockSubCommand dock,
        IMessageBus bus,
        CancellationToken ct,
        string onward = "")
        => (await LegTowardsAsync(map, ship, destination, dock, bus, ct, onward)).Result;

    /// <summary>
    /// Takes the ship one leg towards a waypoint, as <see cref="TradeRoutePlanner.TryPlanNextLeg"/> plans it. A ship in orbit
    /// at a market that sells fuel, short of a full tank, docks first when a full tank would fly the leg differently
    /// (further, or in BURN): only a docked ship fills its tank before it leaves. Without a way known, it flies straight
    /// there in CRUISE, and the navigation does what it can.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="dock">Docks the ship, to refuel before it leaves.</param>
    /// <param name="bus">Sends the navigation.</param>
    /// <param name="ct">Stops the work.</param>
    /// <param name="onward">Where the ship must get to from <paramref name="destination"/> next, when the caller knows.</param>
    /// <returns>The step's outcome, and the leg it flies (for a docking step, the leg the full tank will fly).</returns>
    public static async Task<(GoalExecutionResult Result, FlightLeg Leg)> LegTowardsAsync(
        TradeMarketMap map,
        ShipModel ship,
        string destination,
        IDockSubCommand dock,
        IMessageBus bus,
        CancellationToken ct,
        string onward = "")
    {
        var here = ship.WaypointSymbol ?? string.Empty;
        var planned = TradeRoutePlanner.TryPlanNextLeg(map, ship, destination, onward, out var leg);
        if (ship.LocalStatus == ShipLocalStatus.InOrbit
            && map.SellsFuel(here)
            && ship.FuelCurrent < ship.FuelCapacity
            && TradeRoutePlanner.TryPlanNextLeg(map, ship with { Status = "DOCKED" }, destination, onward, out var filled)
            && (!planned || filled != leg))
        {
            await dock.ExecuteAsync(ship.Symbol, ct);
            return (GoalExecutionResult.Progressing($"Docking at {here} to refuel before flying to {destination}."), filled);
        }

        if (!planned)
        {
            leg = new FlightLeg(destination, TradeRoutePlanner.CruiseMode);
        }

        await bus.InvokeAsync(new NavigateToWaypointCommand(ship.Symbol, leg.WaypointSymbol) { FlightMode = leg.FlightMode }, ct);
        var mode = leg.FlightMode == TradeRoutePlanner.CruiseMode ? string.Empty : $" in {leg.FlightMode}";
        return (
            GoalExecutionResult.WaitingForArrival(leg.WaypointSymbol.Equals(destination, StringComparison.OrdinalIgnoreCase)
                ? $"Navigating to {destination}{mode}."
                : $"Navigating to {destination}, by {leg.WaypointSymbol}{mode}."),
            leg);
    }
}

using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// How the contract's commands fly (<see cref="MineResourceVolumeCommand"/>, <see cref="FulfillContractDeliveryCommand"/>):
/// as a goal's flights do (<see cref="TradeRoutePlanner.TryPlanNextLeg"/>, D84), in BURN where the fuel allows it and
/// strands nothing, in CRUISE otherwise, through refuelling stops when the fuel aboard won't reach, so a ship left in DRIFT
/// flies on in the mode asked for (B47). Their flights carry no goal, so no arrival wakes them:
/// the next tick's command dead-reckons the arrival (B17) and flies the next leg from where the ship is.
/// </summary>
internal static class CommandFlight
{
    /// <summary>
    /// Whether the ship docks to fill its tank before it flies on, as a goal's flight does: it is in orbit at a market that
    /// sells fuel, short of a full tank, and a full tank would fly the next leg differently (further, or in BURN, D84). A
    /// dead-reckoned arrival at a refuelling stop leaves the ship in orbit, and only a docked ship can refuel.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <returns>True when the ship should dock and refuel first.</returns>
    public static bool DocksToRefuel(TradeMarketMap map, ShipModel ship, string destination)
        => ship.LocalStatus == ShipLocalStatus.InOrbit
            && ship.FuelCurrent < ship.FuelCapacity
            && map.SellsFuel(ship.WaypointSymbol ?? string.Empty)
            && TradeRoutePlanner.TryPlanNextLeg(map, ship with { Status = "DOCKED" }, destination, string.Empty, out var filled)
            && !(TradeRoutePlanner.TryPlanNextLeg(map, ship, destination, string.Empty, out var leg) && leg == filled);

    /// <summary>
    /// Flies a ship in orbit one leg towards a waypoint, as the route planner plans it (D84): straight there when the fuel
    /// aboard will do, otherwise to the first refuelling stop, in BURN where that strands nothing; where no chain of fuel
    /// markets reaches the waypoint, the fastest way, which cruises as far as it can and drifts the rest.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, in orbit.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="flightMode">Switches a ship in another mode to the leg's.</param>
    /// <param name="navigate">Flies the leg.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>A task that completes once the ship is on its way.</returns>
    public static async Task TowardsAsync(
        TradeMarketMap map,
        ShipModel ship,
        string destination,
        IFlightModeSubCommand flightMode,
        INavigateSubCommand navigate,
        CancellationToken ct)
    {
        var leg = TradeRoutePlanner.TryPlanNextLeg(map, ship, destination, string.Empty, out var next)
            ? next
            : new FlightLeg(destination, TradeRoutePlanner.CruiseMode);
        await flightMode.EnsureAsync(ship, leg.FlightMode, ct);
        await navigate.ExecuteAsync(ship.Symbol, leg.WaypointSymbol, Guid.Empty, ct);
    }
}

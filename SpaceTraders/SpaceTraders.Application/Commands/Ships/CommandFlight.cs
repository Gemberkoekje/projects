using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Commands.Ships;

/// <summary>
/// How the contract's commands fly (<see cref="MineResourceVolumeCommand"/>, <see cref="FulfillContractDeliveryCommand"/>):
/// in CRUISE, through refuelling stops when the fuel aboard won't reach (<see cref="TradeRoutePlanner"/>), as a goal's
/// flights do, so a ship left in DRIFT flies in CRUISE again (B47). Their flights carry no goal, so no arrival wakes them:
/// the next tick's command dead-reckons the arrival (B17) and flies the next leg from where the ship is.
/// </summary>
internal static class CommandFlight
{
    private const string CruiseMode = "CRUISE";

    /// <summary>
    /// Whether the ship docks to fill its tank before it flies on: it is in orbit at a market that sells fuel, short of a
    /// full tank, and the fuel aboard won't take it straight to its destination. A dead-reckoned arrival at a refuelling stop
    /// leaves the ship in orbit, and only a docked ship can refuel.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="destination">Where it is going.</param>
    /// <returns>True when the ship should dock and refuel first.</returns>
    public static bool DocksToRefuel(TradeMarketMap map, ShipModel ship, string destination)
        => ship.LocalStatus == ShipLocalStatus.InOrbit
            && ship.FuelCurrent < ship.FuelCapacity
            && map.SellsFuel(ship.WaypointSymbol ?? string.Empty)
            && !(TradeRoutePlanner.TryPlanFlight(map, ship, destination, out var flight) && flight.Stops.Count == 1);

    /// <summary>
    /// Flies a ship in orbit one leg towards a waypoint, in CRUISE: straight there when the fuel aboard will do, otherwise to
    /// the first refuelling stop. Where no chain of fuel markets reaches the waypoint, the navigation does what it can: its
    /// fallback drifts, and the ship's next flight asks for CRUISE again.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, in orbit.</param>
    /// <param name="destination">Where it is going.</param>
    /// <param name="flightMode">Switches a ship in another mode to CRUISE.</param>
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
        await flightMode.EnsureAsync(ship, CruiseMode, ct);
        await navigate.ExecuteAsync(ship.Symbol, TradeRoutePlanner.NextStop(map, ship, destination), Guid.Empty, ct);
    }
}

using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Mining;

/// <summary>
/// The ship the plans buy to mine (PLAN.md slice 6.39, D120): asked on 2026-10-09, "When available, use ORE HOUNDS instead of
/// MINING DRONES." An ore hound wherever a shipyard the plan buys from sells one, the cheapest such; a mining drone only where
/// none does. Neither where the shipyard has it SCARCE (D121).
/// </summary>
public static class MinerShips
{
    /// <summary>The mining drone, bought where no ore hound is sold.</summary>
    public const string MiningDroneShipType = "SHIP_MINING_DRONE";

    /// <summary>The ships the plans buy to mine, the one they prefer first.</summary>
    public static readonly IReadOnlyList<string> Preferred = [FleetRoles.OreHoundShipType, MiningDroneShipType];

    /// <summary>
    /// The listing of the miner to buy among <paramref name="shipyards"/>: the cheapest ore hound with a known price that isn't
    /// SCARCE, else the cheapest such mining drone; of equal prices the shipyard first by symbol.
    /// </summary>
    /// <param name="shipyards">The shipyards the plan may buy at, as cached.</param>
    /// <param name="usable">What else the listing needs, such as its tank listed; none when null.</param>
    /// <returns>The shipyard and the ship as it lists it; null when none of them sells a miner.</returns>
    public static (ShipyardWaypointDto Shipyard, ShipyardShipDto Ship)? Cheapest(
        IEnumerable<ShipyardWaypointDto> shipyards,
        Func<ShipyardShipDto, bool>? usable = null)
    {
        ArgumentNullException.ThrowIfNull(shipyards);

        var listed = shipyards.ToList();
        foreach (var type in Preferred)
        {
            var listing = listed
                .SelectMany(shipyard => shipyard.Ships
                    .Where(ship => ship.Type.Equals(type, StringComparison.OrdinalIgnoreCase)
                        && ship.PurchasePrice > 0
                        && !ScarceShips.IsScarce(ship)
                        && (usable?.Invoke(ship) ?? true))
                    .Select(ship => (Shipyard: shipyard, Ship: ship)))
                .OrderBy(candidate => candidate.Ship.PurchasePrice)
                .ThenBy(candidate => candidate.Shipyard.WaypointSymbol, StringComparer.Ordinal)
                .FirstOrDefault();
            if (listing.Shipyard is not null)
            {
                return listing;
            }
        }

        return null;
    }

    /// <summary>
    /// The miner as the plans reckon with it before it is bought: at the shipyard, docked, with the tank, hold and mounts its
    /// listing shows, and its type.
    /// </summary>
    /// <param name="shipyard">Where it would be bought.</param>
    /// <param name="ship">The ship as the shipyard lists it.</param>
    /// <param name="symbol">The symbol to give it.</param>
    /// <returns>The ship.</returns>
    public static ShipModel AsBought(ShipyardWaypointDto shipyard, ShipyardShipDto ship, string symbol = "NEW-DRONE")
    {
        ArgumentNullException.ThrowIfNull(shipyard);
        ArgumentNullException.ThrowIfNull(ship);
        return new ShipModel(
            symbol,
            shipyard.SystemSymbol,
            shipyard.WaypointSymbol,
            "DOCKED",
            "CRUISE",
            ship.FuelCapacity,
            ship.FuelCapacity,
            CargoCapacity: ship.CargoCapacity,
            ShipType: ship.Type.ToUpperInvariant(),
            MountSymbols: ship.Mounts);
    }
}

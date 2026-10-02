using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.SpareTime;

/// <summary>
/// The spare-time choices (PLAN.md slice 6.8), without any I/O, so the plans, the trip and their tests decide alike:
/// <list type="bullet">
///   <item>a ship gathers at the nearest place it can work (D35): an asteroid with its mining laser, a gas giant with
///   its gas siphon, that it can reach through refuelling stops and that yields a good a market it can carry it to
///   from there buys. It keeps every such good, whatever it is (<see cref="MiningPlanner.IsSellableFrom"/>), and no
///   survey guides it: the surveys stay for the drones;</item>
///   <item>it sells its hold one good at a time, each where it fetches most after the fuel to get there (D36): the
///   rule the trading plan sells held cargo by (<see cref="TradeRoutePlanner.TryFindBestCargoSale"/>);</item>
///   <item>the trading plan takes it for a route only when the route is lucrative from where selling its hold
///   leaves it (<see cref="AfterSellingHold"/>), so a trade that interrupts it (D34, D37) is still there once the
///   hold is sold.</item>
/// </list>
/// </summary>
public static class GatherPlanner
{
    /// <summary>What a spare-time trip mines or siphons for, as the <c>Extracted</c> and <c>Siphoned</c> lines name it (<c>Target</c>).</summary>
    public const string AnyGood = "whatever sells";

    /// <summary>
    /// Where a ship would gather (D35): the nearest asteroid (with a mining laser) or gas giant (with a gas siphon)
    /// that it can reach and that yields a good a market it can carry it to buys. A tie goes to the first by symbol.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now.</param>
    /// <param name="source">The asteroid or gas giant, and how far it is.</param>
    /// <returns>False when there is none.</returns>
    public static bool TryFindSource(TradeMarketMap map, ShipModel ship, out GatherSource source)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        var position = MiningPlanner.Position(ship);
        var mines = FleetRoles.HasMiningLaser(ship);
        var siphons = ship.HasGasSiphonEquipment;
        var nearest = map.Waypoints
            .Where(waypoint => (mines && AsteroidDeposits.IsExtractable(waypoint.Type)) || (siphons && GasGiants.IsSiphonable(waypoint.Type)))
            .Select(waypoint => (Waypoint: waypoint, Distance: map.TryGetDistance(position, waypoint.Symbol, out var distance) ? distance : double.MaxValue))
            .Where(candidate => candidate.Distance < double.MaxValue)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Waypoint.Symbol, StringComparer.Ordinal)
            .FirstOrDefault(candidate => MiningPlanner.CanReach(map, ship, candidate.Waypoint.Symbol) && YieldsSellable(map, ship, candidate.Waypoint));

        if (nearest.Waypoint is null)
        {
            source = new GatherSource(string.Empty, Siphoning: false, 0);
            return false;
        }

        source = new GatherSource(nearest.Waypoint.Symbol, GasGiants.IsSiphonable(nearest.Waypoint.Type), nearest.Distance);
        return true;
    }

    /// <summary>
    /// Whether a ship gathering at a waypoint would keep anything: the waypoint yields a good that a market the ship
    /// can carry it to from there buys. A trip whose source no longer does ends, and the plan chooses again.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship.</param>
    /// <param name="sourceWaypointSymbol">The asteroid or gas giant.</param>
    /// <returns>False for a waypoint that isn't on the map, or that yields nothing sellable.</returns>
    public static bool YieldsSellable(TradeMarketMap map, ShipModel ship, string sourceWaypointSymbol)
    {
        ArgumentNullException.ThrowIfNull(map);

        var waypoint = map.Waypoints.FirstOrDefault(candidate => candidate.Symbol.Equals(sourceWaypointSymbol, StringComparison.OrdinalIgnoreCase));
        return waypoint is not null && YieldsSellable(map, ship, waypoint);
    }

    /// <summary>
    /// The ship as it would be once it has sold its hold the way the trading plan sells held cargo (D36,
    /// <see cref="TradeRoutePlanner.TryFindBestCargoSale"/>): one good after another, each where it fetches most after
    /// fuel, from where the last sale left it, docked at the last market. What doesn't pay for its fuel stays aboard.
    /// With an empty hold, the ship as it is.
    /// </summary>
    /// <param name="map">The ship's system.</param>
    /// <param name="ship">The ship, where it is now, with its hold.</param>
    /// <returns>The ship after its sales, at the prices last seen.</returns>
    public static ShipModel AfterSellingHold(TradeMarketMap map, ShipModel ship)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ship);

        var current = ship;
        while (TradeRoutePlanner.TryFindBestCargoSale(map, current, mustSell: false, out var cargo, out var sale))
        {
            var fuelLeft = TradeRoutePlanner.TryPlanFlight(map, current, sale.WaypointSymbol, out var flight) ? flight.FuelLeft : current.FuelCurrent;
            List<CargoItemModel> left =
            [
                .. (current.CargoInventory ?? []).Where(item => item.Units > 0 && !item.Symbol.Equals(cargo.Symbol, StringComparison.OrdinalIgnoreCase)),
            ];
            current = current with
            {
                WaypointSymbol = sale.WaypointSymbol,
                Status = "DOCKED",
                DestWaypointSymbol = null,
                ArrivesAt = null,
                FuelCurrent = fuelLeft,
                CargoInventory = left,
                CargoCurrent = left.Sum(item => item.Units),
            };
        }

        return current;
    }

    private static bool YieldsSellable(TradeMarketMap map, ShipModel ship, WaypointCacheModel waypoint)
    {
        var goods = GasGiants.IsSiphonable(waypoint.Type) ? GasGiants.GasesAt(waypoint) : AsteroidDeposits.OresAt(waypoint);
        return goods.Any(good => MiningPlanner.IsSellableFrom(map, ship, waypoint.Symbol, good));
    }
}

/// <summary>Where a ship gathers in its spare time: an asteroid or a gas giant.</summary>
public sealed record GatherSource
{
    /// <summary>Creates a source.</summary>
    /// <param name="WaypointSymbol">The asteroid or gas giant.</param>
    /// <param name="Siphoning">True for a gas giant, where the ship siphons; false for an asteroid, where it mines.</param>
    /// <param name="Distance">How far it is from the ship.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GatherSource(string WaypointSymbol, bool Siphoning, double Distance)
    {
        this.WaypointSymbol = WaypointSymbol;
        this.Siphoning = Siphoning;
        this.Distance = Distance;
    }

    /// <summary>The asteroid or gas giant.</summary>
    public required string WaypointSymbol { get; init; }

    /// <summary>True for a gas giant, where the ship siphons; false for an asteroid, where it mines.</summary>
    public required bool Siphoning { get; init; }

    /// <summary>How far it is from the ship.</summary>
    public required double Distance { get; init; }
}

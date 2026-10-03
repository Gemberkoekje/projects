using System.Globalization;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// The credits every ship purchase must leave (PLAN.md slice 6.10b, D51): <c>FleetExpansion.MinCreditReserve</c> as the
/// floor (60,000 seeded), and <c>FleetExpansion.ReservePerTradingCargoUnit</c> (1,000) for every unit the ships that trade
/// can carry, so the traders can still buy their loads after a purchase. Asked on 2026-10-03: "What if we made the amount
/// of credits for trade wider based on the amount of cargo total in the fleet? Something like: 60.000 hard minimum, 1.000
/// per cargo hold."
/// </summary>
/// <remarks>
/// The ships that trade: the cargo ships (<see cref="FleetRoles.IsCargoShip"/>), the command ship, which trades whenever it
/// isn't surveying (D34, D38), and any other ship the role board has in the trade role. Drones that gather, probes and
/// surveyors buy no cargo, so their holds don't count: with every hold counted, the 205 units of 2026-10-03 would have
/// asked 265,000, above what the credits peaked at, and frozen every purchase.
/// </remarks>
public static class CreditReserve
{
    /// <summary>The setting that holds the floor: the reserve with no ship that trades.</summary>
    public const string FloorSetting = "FleetExpansion.MinCreditReserve";

    /// <summary>The setting that holds the credits kept for every unit the ships that trade can carry.</summary>
    public const string PerTradingCargoUnitSetting = "FleetExpansion.ReservePerTradingCargoUnit";

    /// <summary>The credits per unit when the setting gives none: 1,000 (D51).</summary>
    internal const long DefaultPerTradingCargoUnit = 1_000;

    /// <summary>The command ship's registration role, its cached type after startup sync.</summary>
    private const string CommandShipType = "COMMAND";

    /// <summary>The reserve: the floor, and the credits per unit for every unit the ships that trade can carry.</summary>
    /// <param name="floor">The floor (<see cref="FloorSetting"/>); below 0 counts as 0.</param>
    /// <param name="perUnit">The credits per unit (<see cref="PerTradingCargoUnitSetting"/>); below 0 counts as 0.</param>
    /// <param name="tradingCargo">What the ships that trade can carry (<see cref="TradingCargo"/>).</param>
    /// <returns>The credits a purchase must leave.</returns>
    public static long Of(long floor, long perUnit, int tradingCargo)
        => Math.Max(0, floor) + (Math.Max(0, perUnit) * Math.Max(0, tradingCargo));

    /// <summary>What the ships that trade can carry, in units (<see cref="Trades"/>).</summary>
    /// <param name="fleet">The fleet.</param>
    /// <param name="roleOf">Each ship's role on the role board; <see cref="FleetRole.None"/> with the board off.</param>
    /// <returns>Their cargo capacity together.</returns>
    public static int TradingCargo(IEnumerable<ShipModel> fleet, Func<ShipModel, FleetRole> roleOf)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(roleOf);
        return fleet.Where(ship => Trades(ship, roleOf(ship))).Sum(ship => ship.CargoCapacity);
    }

    /// <summary>Whether a ship buys cargo: a cargo ship, the command ship, or a ship the role board has trading.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="role">Its role on the role board; <see cref="FleetRole.None"/> with the board off.</param>
    /// <returns>True when its hold counts towards the reserve.</returns>
    public static bool Trades(ShipModel ship, FleetRole role)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return FleetRoles.IsCargoShip(ship)
            || ship.ShipType.Equals(CommandShipType, StringComparison.OrdinalIgnoreCase)
            || role == FleetRole.Trade;
    }

    /// <summary>The credits per unit from the setting's stored value: the default without one, 0 or more otherwise.</summary>
    /// <param name="raw">The stored value; empty when the setting is missing.</param>
    /// <returns>The credits per unit.</returns>
    public static long PerTradingCargoUnit(string raw)
        => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : DefaultPerTradingCargoUnit;
}

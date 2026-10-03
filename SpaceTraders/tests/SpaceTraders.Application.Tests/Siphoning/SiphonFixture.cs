using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Siphoning;

/// <summary>
/// The gas side of X1-DC53 for the siphon tests, at the positions the API gave on 2026-10-02; the prices and
/// supply levels are made up, as none of our ships has been there since scouting:
/// <list type="bullet">
///   <item>the gas giant C38, and C39, the orbital station at the same spot, which sells siphon drones and
///   exchanges the three gases (all MODERATE here);</item>
///   <item>C40, a fuel station 39 from C38, through which a drone's 80-unit tank reaches G50 (imports all three
///   gases: LIQUID_HYDROGEN SCARCE, HYDROCARBON LIMITED) and E47 (LIQUID_NITROGEN SCARCE);</item>
///   <item>F48, far south, which imports LIQUID_NITROGEN (SCARCE, and pays most) and LIQUID_HYDROGEN (LIMITED).
///   This fixture leaves out the middle of the system, through which a drone could refuel on the way, so only
///   the command ship's 400-unit tank gets there. C38 is 228 from it, so no drone drifts there to siphon (D45);
///   with <see cref="MapWithAGasGiantNearF48"/>, one does.</item>
/// </list>
/// </summary>
internal static class SiphonFixture
{
    public const string SystemSymbol = "X1-DC53";
    public const string C38 = "X1-DC53-C38";
    public const string C39 = "X1-DC53-C39";
    public const string C40 = "X1-DC53-C40";
    public const string G50 = "X1-DC53-G50";
    public const string E47 = "X1-DC53-E47";
    public const string F48 = "X1-DC53-F48";

    /// <summary>A gas giant X1-DC53 doesn't have, 13 from F48 (<see cref="MapWithAGasGiantNearF48"/>).</summary>
    public const string D90 = "X1-DC53-D90";

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(C38, "GAS_GIANT", -57, -143, """[{"symbol":"STRONG_MAGNETOSPHERE"}]"""),
        Waypoint(C39, "ORBITAL_STATION", -57, -143, """[{"symbol":"MARKETPLACE"},{"symbol":"SHIPYARD"}]"""),
        Waypoint(C40, "FUEL_STATION", -42, -107),
        Waypoint(G50, "PLANET", 6, -66),
        Waypoint(E47, "MOON", 4, -54),
        Waypoint(F48, "PLANET", 24, 70),
    ];

    public static MarketSnapshot[] Markets() =>
    [
        Market(
            C39,
            Good("HYDROCARBON", "EXCHANGE", 70, 60, 60, "MODERATE"),
            Good("LIQUID_HYDROGEN", "EXCHANGE", 40, 35, 60, "MODERATE"),
            Good("LIQUID_NITROGEN", "EXCHANGE", 34, 30, 60, "MODERATE"),
            Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
        Market(C40, Good("FUEL", "EXCHANGE", 75, 66, 180, "MODERATE")),
        Market(
            G50,
            Good("HYDROCARBON", "IMPORT", 180, 90, 60, "LIMITED"),
            Good("LIQUID_HYDROGEN", "IMPORT", 110, 55, 60, "SCARCE"),
            Good("LIQUID_NITROGEN", "IMPORT", 90, 45, 60, "MODERATE"),
            Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE")),
        Market(
            E47,
            Good("LIQUID_NITROGEN", "IMPORT", 100, 50, 60, "SCARCE"),
            Good("LIQUID_HYDROGEN", "IMPORT", 96, 48, 60, "MODERATE"),
            Good("FUEL", "EXCHANGE", 81, 71, 180, "MODERATE")),
        Market(
            F48,
            Good("LIQUID_NITROGEN", "IMPORT", 120, 60, 60, "SCARCE"),
            Good("LIQUID_HYDROGEN", "IMPORT", 104, 52, 60, "LIMITED"),
            Good("FUEL", "EXCHANGE", 79, 70, 180, "MODERATE")),
    ];

    public static TradeMarketMap Map(params MarketSnapshot[] markets)
        => new(Waypoints, markets.Length == 0 ? Markets() : markets, new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>
    /// The system with a second gas giant, D90, 13 from F48: F48, beyond a drone's tank, sells fuel, and D90 is there and
    /// back on one tank, so a drone would drift to F48 to siphon from there (D45).
    /// </summary>
    public static TradeMarketMap MapWithAGasGiantNearF48(params MarketSnapshot[] markets)
        => new(
            [.. Waypoints, Waypoint(D90, "GAS_GIANT", 30, 82, """[{"symbol":"STRONG_MAGNETOSPHERE"}]""")],
            markets.Length == 0 ? Markets() : markets,
            new Dictionary<string, IReadOnlyList<string>>());

    public static TradeContext Context(params MarketSnapshot[] markets) => new(Map(markets), 250_000, 200);

    /// <summary>A siphon drone, docked at C39 where it was bought: a gas siphon, a 15-unit hold and an 80-unit tank.</summary>
    public static ShipModel SiphonDrone(string symbol = "SHIP-5", string waypoint = C39, string status = "DOCKED", IReadOnlyList<CargoItemModel>? cargo = null)
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            status,
            "CRUISE",
            80,
            80,
            CargoCurrent: (cargo ?? []).Sum(item => item.Units),
            CargoCapacity: 15,
            ShipType: "SHIP_SIPHON_DRONE",
            MountSymbols: ["MOUNT_GAS_SIPHON_I"],
            CargoInventory: cargo ?? []);

    /// <summary>The command ship: a gas siphon, but also a mining laser and a surveyor; a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(string waypoint = C39, string status = "DOCKED", string symbol = "SHIP-1")
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            status,
            "CRUISE",
            400,
            400,
            CargoCapacity: 40,
            ShipType: "COMMAND",
            MountSymbols: ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II", "MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"],
            CargoInventory: []);

    public static MarketSnapshot Market(string waypoint, params TradeGoodSnapshot[] goods)
        => new(
            waypoint,
            SystemSymbol,
            goods,
            [.. goods.Where(good => good.Type == "IMPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXCHANGE").Select(good => good.Symbol)]);

    public static TradeGoodSnapshot Good(string symbol, string type, int purchasePrice, int sellPrice, int tradeVolume, string supply)
        => new(symbol, type, purchasePrice, sellPrice, tradeVolume, supply);

    private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y, string traits = """[{"symbol":"MARKETPLACE"}]""")
        => new(symbol, SystemSymbol, type, x, y, traits.Contains("MARKETPLACE", StringComparison.Ordinal), symbol == C39, DateTimeOffset.UnixEpoch, TraitsJson: traits);
}

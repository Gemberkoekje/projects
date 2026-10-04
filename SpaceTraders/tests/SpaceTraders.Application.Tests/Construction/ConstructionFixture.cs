using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Construction;

/// <summary>
/// The jump gate side of X1-DC53 for the construction tests (slice 6.6), at the positions the API gave on 2026-10-04; the
/// prices, supplies and trade volumes are made up. The gate I55 itself was complete then (FAB_MATS 1600/1600,
/// ADVANCED_CIRCUITRY 400/400, QUANTUM_STABILIZERS 1/1); the site here is what an unbuilt one needs, as its neighbours
/// X1-HZ59-I59 and X1-BG54-I54 did:
/// <list type="bullet">
///   <item>the jump gate I55 far out at (272, -358), whose market sells fuel;</item>
///   <item>F49 exports FAB_MATS (2,100, 80 at a time, MODERATE), 495 from the gate; A1 imports it, dearer and LIMITED;</item>
///   <item>D42 exports ADVANCED_CIRCUITRY (4,500, 40 at a time, MODERATE), 530 from the gate;</item>
///   <item>the fuel station I56 between them and the gate (278 from F49, 219 from the gate), and H51 in the middle, where the
///   ships start. A light hauler's 600-unit tank flies from F49 to the gate in one go; a 300-unit one stops at I56.</item>
/// </list>
/// </summary>
internal static class ConstructionFixture
{
    public const string SystemSymbol = "X1-DC53";
    public const string Gate = "X1-DC53-I55";
    public const string F49 = "X1-DC53-F49";
    public const string D42 = "X1-DC53-D42";
    public const string A1 = "X1-DC53-A1";
    public const string I56 = "X1-DC53-I56";
    public const string H51 = "X1-DC53-H51";

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(Gate, "JUMP_GATE", 272, -358, underConstruction: true),
        Waypoint(F49, "ORBITAL_STATION", 24, 70),
        Waypoint(D42, "MOON", -68, 49),
        Waypoint(A1, "PLANET", 21, 16),
        Waypoint(I56, "FUEL_STATION", 140, -183),
        Waypoint(H51, "PLANET", -18, 40),
    ];

    public static MarketSnapshot GateMarket() => Market(Gate, Good("FUEL", "EXCHANGE", 90, 80, 180, "MODERATE"));

    public static MarketSnapshot F49Market(string supply = "MODERATE", int tradeVolume = 80, int price = 2_100) => Market(
        F49,
        Good("FAB_MATS", "EXPORT", price, 1_050, tradeVolume, supply),
        Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE"));

    public static MarketSnapshot D42Market(string supply = "MODERATE", int tradeVolume = 40, int price = 4_500) => Market(
        D42,
        Good("ADVANCED_CIRCUITRY", "EXPORT", price, 2_200, tradeVolume, supply),
        Good("FUEL", "EXCHANGE", 85, 75, 180, "MODERATE"));

    public static MarketSnapshot A1Market(string supply = "LIMITED", int price = 5_000) => Market(
        A1,
        Good("FAB_MATS", "IMPORT", price, 3_000, 80, supply),
        Good("FUEL", "EXCHANGE", 90, 80, 180, "MODERATE"));

    public static MarketSnapshot I56Market() => Market(I56, Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE"));

    public static MarketSnapshot H51Market() => Market(H51, Good("FUEL", "EXCHANGE", 95, 80, 180, "MODERATE"));

    public static MarketSnapshot[] Markets() => [GateMarket(), F49Market(), D42Market(), A1Market(), I56Market(), H51Market()];

    public static TradeMarketMap Map(params MarketSnapshot[] markets)
        => new(Waypoints, markets.Length == 0 ? Markets() : markets, new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>An unbuilt jump gate: 1,600 FAB_MATS, 400 ADVANCED_CIRCUITRY, and the one QUANTUM_STABILIZERS it comes with.</summary>
    public static ConstructionSiteModel Site(int fabMats = 0, int circuitry = 0, bool complete = false)
        => new(
            Gate,
            complete,
            [
                new ConstructionMaterialModel("FAB_MATS", 1_600, fabMats),
                new ConstructionMaterialModel("ADVANCED_CIRCUITRY", 400, circuitry),
                new ConstructionMaterialModel("QUANTUM_STABILIZERS", 1, 1),
            ]);

    /// <summary>A light hauler: an 80-unit hold, a 600-unit tank, nothing to mine, siphon or survey with.</summary>
    public static ShipModel Hauler(string symbol = "SHIP-6", string waypoint = H51, string status = "DOCKED", IReadOnlyList<CargoItemModel>? cargo = null)
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            status,
            "CRUISE",
            600,
            600,
            CargoCurrent: (cargo ?? []).Sum(item => item.Units),
            CargoCapacity: 80,
            ShipType: "SHIP_LIGHT_HAULER",
            MountSymbols: ["MOUNT_TURRET_I"],
            CargoInventory: cargo ?? []);

    /// <summary>A light shuttle: a 40-unit hold and a 300-unit tank.</summary>
    public static ShipModel Shuttle(string symbol = "SHIP-7", string waypoint = H51)
        => new(symbol, SystemSymbol, waypoint, "DOCKED", "CRUISE", 300, 300, CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: ["MOUNT_TURRET_I"], CargoInventory: []);

    /// <summary>The command ship: a surveyor, a mining laser and a gas siphon, a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(string symbol = "SHIP-1", string waypoint = H51)
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            "DOCKED",
            "CRUISE",
            400,
            400,
            CargoCapacity: 40,
            ShipType: "COMMAND",
            MountSymbols: ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II", "MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"],
            CargoInventory: []);

    /// <summary>A mining drone: a 15-unit hold and an 80-unit tank.</summary>
    public static ShipModel Drone(string symbol = "SHIP-3", string waypoint = H51)
        => new(symbol, SystemSymbol, waypoint, "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "EXCAVATOR", MountSymbols: ["MOUNT_MINING_LASER_I"], CargoInventory: []);

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

    private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y, bool underConstruction = false)
        => new(symbol, SystemSymbol, type, x, y, HasMarket: true, HasShipyard: false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"MARKETPLACE"}]""", IsUnderConstruction: underConstruction);
}

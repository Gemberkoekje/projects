using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// A small system for the survey and mining tests, with the positions and prices X1-DC53 had on
/// 2026-10-02:
/// <list type="bullet">
///   <item>the engineered asteroid XB5C (COMMON_METAL_DEPOSITS) in the middle, which sells fuel at 97;</item>
///   <item>near it H51, which imports COPPER_ORE (pays 67) and IRON_ORE (58) in LIMITED supply and
///   ALUMINUM_ORE (63) in MODERATE; F49, which imports QUARTZ_SAND (26) and SILICON_CRYSTALS (49), both
///   SCARCE; and H52, which sells mining drones;</item>
///   <item>far out, B7, which imports GOLD_ORE (114) and exchanges COPPER_ORE (58), both SCARCE, with the
///   asteroids B14 (PRECIOUS_METAL_DEPOSITS) and B13 (COMMON_METAL_DEPOSITS) next to it. A drone's 80-unit
///   tank doesn't get it there: no chain of fuel markets 80 apart leads out of the middle.</item>
/// </list>
/// </summary>
internal static class MiningFixture
{
    public const string SystemSymbol = "X1-DC53";
    public const string XB5C = "X1-DC53-XB5C";
    public const string H51 = "X1-DC53-H51";
    public const string H52 = "X1-DC53-H52";
    public const string F49 = "X1-DC53-F49";
    public const string B7 = "X1-DC53-B7";
    public const string B13 = "X1-DC53-B13";
    public const string B14 = "X1-DC53-B14";

    public static readonly DateTimeOffset Now = new(2026, 10, 02, 14, 00, 00, TimeSpan.Zero);

    private const string CommonMetals = """[{"symbol":"COMMON_METAL_DEPOSITS"},{"symbol":"MARKETPLACE"}]""";

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(XB5C, "ENGINEERED_ASTEROID", -15, 21, CommonMetals),
        Waypoint(H51, "PLANET", -18, 40),
        Waypoint(H52, "MOON", -18, 40),
        Waypoint(F49, "ORBITAL_STATION", 24, 70),
        Waypoint(B7, "ASTEROID_BASE", 47, 343),
        Waypoint(B13, "ASTEROID", 18, 381, """[{"symbol":"COMMON_METAL_DEPOSITS"}]"""),
        Waypoint(B14, "ASTEROID", 23, 348, """[{"symbol":"PRECIOUS_METAL_DEPOSITS"}]"""),
    ];

    public static MarketSnapshot[] Markets() =>
    [
        Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180, "MODERATE")),
        Market(
            H51,
            Good("COPPER_ORE", "IMPORT", 138, 67, 123, "LIMITED"),
            Good("IRON_ORE", "IMPORT", 118, 58, 100, "LIMITED"),
            Good("ALUMINUM_ORE", "IMPORT", 130, 63, 149, "MODERATE"),
            Good("FUEL", "EXCHANGE", 95, 80, 180, "MODERATE")),
        Market(H52, Good("FUEL", "EXCHANGE", 76, 69, 180, "MODERATE")),
        Market(
            F49,
            Good("QUARTZ_SAND", "IMPORT", 52, 26, 60, "SCARCE"),
            Good("SILICON_CRYSTALS", "IMPORT", 100, 49, 60, "SCARCE"),
            Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE")),
        Market(
            B7,
            Good("GOLD_ORE", "IMPORT", 230, 114, 60, "SCARCE"),
            Good("COPPER_ORE", "EXCHANGE", 68, 58, 180, "SCARCE"),
            Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
    ];

    public static TradeMarketMap Map(params MarketSnapshot[] markets)
        => new(Waypoints, markets.Length == 0 ? Markets() : markets, new Dictionary<string, IReadOnlyList<string>>());

    public static MiningContext Context(params SurveyModel[] surveys) => new(Map(), surveys, 129_357, Now);

    /// <summary>The command ship: a surveyor and a mining laser, a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(string waypoint = XB5C, string status = "IN_ORBIT", string symbol = "SHIP-1")
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

    /// <summary>A survey ship, as bought (D47): a surveyor, an 80-unit tank, and nothing to carry anything in.</summary>
    public static ShipModel SurveyShip(string waypoint = XB5C, string symbol = "SHIP-5")
        => new(symbol, SystemSymbol, waypoint, "IN_ORBIT", "CRUISE", 80, 80, ShipType: "SHIP_SURVEYOR", MountSymbols: ["MOUNT_SURVEYOR_I"]);

    /// <summary>A mining drone: a 15-unit hold and an 80-unit tank.</summary>
    public static ShipModel Drone(string symbol = "SHIP-3", string waypoint = H51, string status = "DOCKED", IReadOnlyList<CargoItemModel>? cargo = null)
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
            ShipType: "EXCAVATOR",
            MountSymbols: ["MOUNT_MINING_LASER_I"],
            CargoInventory: cargo ?? []);

    public static SurveyModel Survey(string signature, string waypoint, params string[] deposits)
        => new(signature, waypoint, [.. deposits.Select(deposit => new SurveyDepositModel(deposit))], Now.AddMinutes(30), "MODERATE");

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
        => new(symbol, SystemSymbol, type, x, y, traits.Contains("MARKETPLACE", StringComparison.Ordinal), symbol == H52, DateTimeOffset.UnixEpoch, TraitsJson: traits);
}

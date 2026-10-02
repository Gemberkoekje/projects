using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.SpareTime;

/// <summary>
/// X1-DC53 for the spare-time tests: the middle, where the command ship surveys (as <c>MiningFixture</c> has it),
/// and the gas side (as <c>SiphonFixture</c> has it), at the positions the API gave on 2026-10-02:
/// <list type="bullet">
///   <item>the engineered asteroid XB5C (COMMON_METAL_DEPOSITS), which sells fuel; H51, 19 from it, imports COPPER_ORE
///   (pays 67), IRON_ORE (58) and ALUMINUM_ORE (63); F49, 63 from it, imports QUARTZ_SAND (26) and SILICON_CRYSTALS
///   (49). No market here buys ICE_WATER;</item>
///   <item>I60, made up: an ASTEROID with ICE_CRYSTALS (ice water and ammonia ice, which no market here buys), 15 from
///   H51, nearer it than XB5C;</item>
///   <item>the gas giant C38, 169 from XB5C, with the station C39 at the same spot, which exchanges the three gases;
///   G50 imports them for more, 99 from C38.</item>
/// </list>
/// </summary>
internal static class SpareTimeFixture
{
    public const string SystemSymbol = "X1-DC53";
    public const string XB5C = "X1-DC53-XB5C";
    public const string I60 = "X1-DC53-I60";
    public const string H51 = "X1-DC53-H51";
    public const string F49 = "X1-DC53-F49";
    public const string C38 = "X1-DC53-C38";
    public const string C39 = "X1-DC53-C39";
    public const string G50 = "X1-DC53-G50";

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(XB5C, "ENGINEERED_ASTEROID", -15, 21, """[{"symbol":"COMMON_METAL_DEPOSITS"},{"symbol":"MARKETPLACE"}]"""),
        Waypoint(I60, "ASTEROID", -19, 25, """[{"symbol":"ICE_CRYSTALS"}]"""),
        Waypoint(H51, "PLANET", -18, 40),
        Waypoint(F49, "ORBITAL_STATION", 24, 70),
        Waypoint(C38, "GAS_GIANT", -57, -143, """[{"symbol":"STRONG_MAGNETOSPHERE"}]"""),
        Waypoint(C39, "ORBITAL_STATION", -57, -143),
        Waypoint(G50, "PLANET", 6, -66),
    ];

    public static MarketSnapshot[] Markets() =>
    [
        Market(XB5C, Good("FUEL", "EXCHANGE", 97, 82, 180)),
        Market(
            H51,
            Good("COPPER_ORE", "IMPORT", 138, 67, 60),
            Good("IRON_ORE", "IMPORT", 118, 58, 60),
            Good("ALUMINUM_ORE", "IMPORT", 130, 63, 60),
            Good("FUEL", "EXCHANGE", 95, 80, 180)),
        Market(
            F49,
            Good("QUARTZ_SAND", "IMPORT", 52, 26, 60),
            Good("SILICON_CRYSTALS", "IMPORT", 100, 49, 60),
            Good("FUEL", "EXCHANGE", 82, 72, 180)),
        Market(
            C39,
            Good("HYDROCARBON", "EXCHANGE", 70, 60, 60),
            Good("LIQUID_HYDROGEN", "EXCHANGE", 40, 35, 60),
            Good("LIQUID_NITROGEN", "EXCHANGE", 34, 30, 60),
            Good("FUEL", "EXCHANGE", 80, 70, 180)),
        Market(
            G50,
            Good("HYDROCARBON", "IMPORT", 180, 90, 60),
            Good("LIQUID_HYDROGEN", "IMPORT", 110, 55, 60),
            Good("LIQUID_NITROGEN", "IMPORT", 90, 45, 60),
            Good("FUEL", "EXCHANGE", 82, 72, 180)),
    ];

    public static TradeMarketMap Map(params MarketSnapshot[] markets)
        => new(Waypoints, markets.Length == 0 ? Markets() : markets, new Dictionary<string, IReadOnlyList<string>>());

    public static TradeContext Context(params MarketSnapshot[] markets) => new(Map(markets), 250_000, 200);

    /// <summary>The command ship: a gas siphon, a mining laser and a surveyor; a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(
        string waypoint = XB5C,
        string status = "IN_ORBIT",
        IReadOnlyList<CargoItemModel>? cargo = null,
        string symbol = "SHIP-1")
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            status,
            "CRUISE",
            400,
            400,
            CargoCurrent: (cargo ?? []).Sum(item => item.Units),
            CargoCapacity: 40,
            ShipType: "COMMAND",
            MountSymbols: ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II", "MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"],
            CargoInventory: cargo ?? []);

    public static MarketSnapshot Market(string waypoint, params TradeGoodSnapshot[] goods)
        => new(
            waypoint,
            SystemSymbol,
            goods,
            [.. goods.Where(good => good.Type == "IMPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXCHANGE").Select(good => good.Symbol)]);

    public static TradeGoodSnapshot Good(string symbol, string type, int purchasePrice, int sellPrice, int tradeVolume)
        => new(symbol, type, purchasePrice, sellPrice, tradeVolume, "MODERATE");

    private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y, string traits = """[{"symbol":"MARKETPLACE"}]""")
        => new(symbol, SystemSymbol, type, x, y, traits.Contains("MARKETPLACE", StringComparison.Ordinal), symbol == C39, DateTimeOffset.UnixEpoch, TraitsJson: traits);
}

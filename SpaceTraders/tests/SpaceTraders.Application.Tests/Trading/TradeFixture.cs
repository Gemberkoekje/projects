using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// A small system for the trading tests, with the positions and prices of three markets in X1-DC53 as
/// the bot saw them on 2026-10-02. EQUIPMENT and MEDICINE traded 20 at a time then; here 40, a command ship's
/// hold, so its trips take a full hold in one go (D56). SHIP_PARTS keep D41's 15:
/// <list type="bullet">
///   <item>K85 exports EQUIPMENT (3,254) and FOOD (2,360), and sells fuel at 93;</item>
///   <item>D41 imports EQUIPMENT (pays 3,487) and makes SHIP_PARTS (7,721) from it, exports MEDICINE
///   (4,867), and sells fuel at 76; 185 from K85;</item>
///   <item>A1 imports EQUIPMENT (pays 3,499), FOOD (2,492) and MEDICINE (5,253), and sells fuel at 90;
///   104 from K85 and 95 from D41;</item>
///   <item>far out, J57 (where the scout plan ended) and I56 sell only fuel, at 93 and 86: J57 is 368
///   from I56 and further than one 400-unit tank from everything else.</item>
/// </list>
/// </summary>
internal static class TradeFixture
{
    public const string SystemSymbol = "X1-AB";
    public const string K85 = "X1-AB-K85";
    public const string D41 = "X1-AB-D41";
    public const string A1 = "X1-AB-A1";
    public const string Asteroid = "X1-AB-B7";
    public const string J57 = "X1-AB-J57";
    public const string I56 = "X1-AB-I56";

    /// <summary>
    /// The production chains: what each good is made from, ships included, as the API's supply chain gives them. Nothing is
    /// made from MEDICINE or FOOD: end products (D82).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MadeFrom = new Dictionary<string, IReadOnlyList<string>>
    {
        ["SHIP_PARTS"] = ["ELECTRONICS", "EQUIPMENT"],
        ["MEDICINE"] = ["FABRICS", "POLYNUCLEOTIDES"],
        ["FOOD"] = ["FERTILIZERS"],
        ["EQUIPMENT"] = ["ALUMINUM", "PLASTICS"],
        ["SHIP_LIGHT_HAULER"] = ["SHIP_PARTS", "SHIP_PLATING"],
    };

    public static IReadOnlyList<WaypointCacheModel> Waypoints =>
    [
        Waypoint(K85, 68, -77),
        Waypoint(D41, -68, 49),
        Waypoint(A1, 21, 16),
        Waypoint(Asteroid, 47, 343, hasMarket: false),
        Waypoint(J57, 362, -476),
        Waypoint(I56, 140, -183),
    ];

    public static MarketSnapshot K85Market(int equipmentPrice = 3_254) => Market(
        K85,
        Good("EQUIPMENT", "EXPORT", equipmentPrice, 1_456, 43),
        Good("FOOD", "EXPORT", 2_360, 1_069, 60),
        Good("FUEL", "EXCHANGE", 93, 79, 180));

    public static MarketSnapshot D41Market(int equipmentPrice = 3_487) => Market(
        D41,
        Good("EQUIPMENT", "IMPORT", 7_032, equipmentPrice, 40),
        Good("SHIP_PARTS", "EXPORT", 7_721, 3_478, 15),
        Good("MEDICINE", "EXPORT", 4_867, 2_227, 40),
        Good("FUEL", "EXCHANGE", 76, 69, 180));

    public static MarketSnapshot A1Market(int medicinePrice = 5_253) => Market(
        A1,
        Good("EQUIPMENT", "IMPORT", 7_052, 3_499, 40),
        Good("FOOD", "IMPORT", 5_028, 2_492, 60),
        Good("MEDICINE", "IMPORT", 10_604, medicinePrice, 40),
        Good("FUEL", "EXCHANGE", 90, 76, 180));

    /// <summary>The far fuel stations, J57 and I56.</summary>
    public static MarketSnapshot[] FarMarkets() =>
    [
        Market(J57, Good("FUEL", "EXCHANGE", 93, 79, 180)),
        Market(I56, Good("FUEL", "EXCHANGE", 86, 74, 180)),
    ];

    /// <summary>
    /// For D74: D41 makes SHIP_PARTS and sells them <paramref name="atD41"/> at a time at 7,721, with its supply as given; A1
    /// pays 8,000 for them, <paramref name="atA1"/> at a time. Nothing else trades at a profit.
    /// </summary>
    public static TradeMarketMap ShipPartsMap(string supplyAtD41 = "ABUNDANT", int atD41 = 15, int atA1 = 40, string supplyAtA1 = "MODERATE")
        => Map(
            K85Market(),
            Market(D41, Good("SHIP_PARTS", "EXPORT", 7_721, 3_478, atD41, supplyAtD41), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            Market(A1, Good("SHIP_PARTS", "IMPORT", 16_000, 8_000, atA1, supplyAtA1), Good("FUEL", "EXCHANGE", 90, 76, 180)));

    public static TradeMarketMap Map(params MarketSnapshot[] markets)
        => new(Waypoints, markets.Length == 0 ? [K85Market(), D41Market(), A1Market()] : markets, MadeFrom);

    public static TradeContext Context(TradeMarketMap map, long credits = 250_000, int minProfitPerUnit = 200, long fuelReserve = 0)
        => new(map, credits, minProfitPerUnit, fuelReserve);

    /// <summary>The command ship: a 40-unit hold and a 400-unit tank.</summary>
    public static ShipModel CommandShip(
        string waypoint = K85,
        string status = "DOCKED",
        int fuel = 400,
        IReadOnlyList<CargoItemModel>? cargo = null,
        string symbol = "SHIP-1")
        => new(
            symbol,
            SystemSymbol,
            waypoint,
            status,
            "CRUISE",
            fuel,
            400,
            CargoCurrent: (cargo ?? []).Sum(item => item.Units),
            CargoCapacity: 40,
            ShipType: "COMMAND",
            MountSymbols: ["MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II"],
            CargoInventory: cargo ?? []);

    /// <summary>The mining drone: a 15-unit hold and an 80-unit tank.</summary>
    public static ShipModel Drone(string waypoint = K85, string symbol = "SHIP-3")
        => new(symbol, SystemSymbol, waypoint, "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "EXCAVATOR", MountSymbols: ["MOUNT_MINING_LASER_I"], CargoInventory: []);

    public static MarketSnapshot Market(string waypoint, params TradeGoodSnapshot[] goods)
        => new(
            waypoint,
            SystemSymbol,
            goods,
            [.. goods.Where(good => good.Type == "IMPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXCHANGE").Select(good => good.Symbol)]);

    public static TradeGoodSnapshot Good(string symbol, string type, int purchasePrice, int sellPrice, int tradeVolume, string supply = "MODERATE")
        => new(symbol, type, purchasePrice, sellPrice, tradeVolume, supply);

    private static WaypointCacheModel Waypoint(string symbol, int x, int y, bool hasMarket = true)
        => new(symbol, SystemSymbol, "PLANET", x, y, hasMarket, false, DateTimeOffset.UnixEpoch);
}

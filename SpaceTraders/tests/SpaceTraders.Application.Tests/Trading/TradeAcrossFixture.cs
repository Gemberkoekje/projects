using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Three systems in a row through their built gates, for trading across systems (PLAN.md slice 6.29, D96): X1-AB is
/// <see cref="TradeFixture"/>'s system, with a gate 82 east of K85; X1-CD, a jump on, has a market 50 from its gate that buys
/// EQUIPMENT and sells PLASTICS; X1-EF, a jump beyond, has one 50 from its gate that buys both. The systems lie 1,000 apart,
/// so a jump's cooldown is about 328 seconds, and each gate sells ANTIMATTER at 5,000 and fuel. The credit floor is 60,000.
/// </summary>
internal static class TradeAcrossFixture
{
    public const string Ab = SystemSymbol;
    public const string Cd = "X1-CD";
    public const string Ef = "X1-EF";
    public const string AbGate = "X1-AB-G";
    public const string CdGate = "X1-CD-G";
    public const string CdMarket = "X1-CD-M";
    public const string EfGate = "X1-EF-G";
    public const string EfMarket = "X1-EF-M";
    public const long Antimatter = 5_000;
    public const long Floor = 60_000;

    /// <summary>When the gates are judged.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 06, 12, 00, 00, TimeSpan.Zero);

    /// <summary>The seconds a jump between two of the systems holds a ship: 17 and 0.311 a unit of their 1,000.</summary>
    public static readonly double Cooldown = TradeGates.CooldownBaseSeconds + (TradeGates.CooldownSecondsPerUnit * 1_000);

    /// <summary>The explore plan's gates: X1-AB to X1-CD to X1-EF, all built and explored.</summary>
    /// <returns>The network.</returns>
    public static ExplorePlanState Network() => new()
    {
        ShipSymbol = "SHIP-1",
        HomeSystemSymbol = Ab,
        Status = ExploreStatus.Done,
        UpdatedAt = Now,
        Systems =
        [
            Known(Ab, AbGate, CdGate),
            Known(Cd, CdGate, AbGate, EfGate),
            Known(Ef, EfGate, CdGate),
        ],
    };

    /// <summary>The ways between the systems.</summary>
    /// <param name="maxJumps">The trade reach, <c>Trade.MaxHaulDistance</c>.</param>
    /// <param name="floor">The credit floor every jump leaves.</param>
    /// <returns>The gates.</returns>
    public static TradeGates Gates(int maxJumps = 5, long floor = Floor)
        => new(
            Network(),
            Now,
            maxJumps,
            new Dictionary<string, long> { [AbGate] = Antimatter, [CdGate] = Antimatter, [EfGate] = Antimatter },
            new Dictionary<string, (int X, int Y)> { [Ab] = (0, 0), [Cd] = (600, 800), [Ef] = (1_200, 1_600) },
            floor);

    /// <summary>
    /// The three systems: TradeFixture's markets at home, X1-CD's market paying <paramref name="cdEquipmentPrice"/> for
    /// EQUIPMENT, 40 at a time, and selling PLASTICS at 500, and X1-EF's paying <paramref name="efEquipmentPrice"/> for EQUIPMENT
    /// and 1,500 for PLASTICS.
    /// </summary>
    /// <param name="cdEquipmentPrice">What X1-CD's market pays for EQUIPMENT.</param>
    /// <param name="efEquipmentPrice">What X1-EF's market pays for EQUIPMENT.</param>
    /// <param name="maxJumps">The trade reach.</param>
    /// <param name="floor">The credit floor.</param>
    /// <param name="staleMarkets">The markets whose prices are too old.</param>
    /// <returns>The map.</returns>
    public static TradeMarketMap AcrossMap(int cdEquipmentPrice = 6_000, int efEquipmentPrice = 3_300, int maxJumps = 5, long floor = Floor, params string[] staleMarkets)
        => new(
            [
                .. Waypoints,
                Place(AbGate, Ab, 150, -77, "JUMP_GATE"),
                Place(CdGate, Cd, 0, 0, "JUMP_GATE"),
                Place(CdMarket, Cd, 30, 40, "PLANET"),
                Place(EfGate, Ef, 0, 0, "JUMP_GATE"),
                Place(EfMarket, Ef, 30, 40, "PLANET"),
            ],
            [
                K85Market(),
                D41Market(),
                A1Market(),
                GateMarket(AbGate, Ab, 90),
                GateMarket(CdGate, Cd, 80),
                GateMarket(EfGate, Ef, 80),
                In(Cd, Market(CdMarket, Good("EQUIPMENT", "IMPORT", 9_000, cdEquipmentPrice, 40), Good("PLASTICS", "EXPORT", 500, 250, 40), Good("FUEL", "EXCHANGE", 80, 70, 180))),
                In(Ef, Market(EfMarket, Good("EQUIPMENT", "IMPORT", 9_000, efEquipmentPrice, 40), Good("PLASTICS", "IMPORT", 3_000, 1_500, 40), Good("FUEL", "EXCHANGE", 80, 70, 180))),
            ],
            MadeFrom)
        {
            Gates = Gates(maxJumps, floor),
            StaleMarkets = staleMarkets.ToHashSet(StringComparer.OrdinalIgnoreCase),
        };

    /// <summary>A system the explore plan knows, explored, with a built gate and its connections.</summary>
    private static KnownSystem Known(string system, string gate, params string[] connections) => new()
    {
        SystemSymbol = system,
        GateWaypointSymbol = gate,
        Gate = GateState.Active,
        GateCheckedAt = Now,
        Connections = connections,
        ConnectionsCheckedAt = Now,
        ExploredAt = Now,
    };

    /// <summary>A gate's market: ANTIMATTER at <see cref="Antimatter"/>, and fuel.</summary>
    private static MarketSnapshot GateMarket(string gate, string system, int fuelPrice)
        => In(system, Market(gate, Good("ANTIMATTER", "EXCHANGE", (int)Antimatter, 4_800, 10), Good("FUEL", "EXCHANGE", fuelPrice, fuelPrice - 10, 180)));

    /// <summary>A market of another system than TradeFixture's.</summary>
    private static MarketSnapshot In(string system, MarketSnapshot market) => market with { SystemSymbol = system };

    private static WaypointCacheModel Place(string symbol, string system, int x, int y, string type)
        => new(symbol, system, type, x, y, true, false, DateTimeOffset.UnixEpoch);
}

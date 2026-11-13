using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.61 (B88, D146): the route work is made faster with the same decisions. Everything the planner answers on a seeded
/// world of six systems through their gates is reduced to one fingerprint, taken from the planner as it was before the
/// slice: every ship's routes as <see cref="TradeRoutePlanner.Rank(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>
/// ranks them and <see cref="TradeRoutePlanner.Judge(TradeMarketMap, ShipModel, long, int, IReadOnlySet{string}, HeldBuys)"/>
/// judges them, at three credit levels and two minimums, with and without held routes; every market's
/// <see cref="TradeRoutePlanner.BestRateThrough"/>; and flights between a sample of markets for four tanks. A change to the
/// planner that moves any of it, a route's order or a figure included, changes the fingerprint.
/// </summary>
/// <remarks>
/// The world exercises what the speed-ups touch: refuelling stops for small tanks, hauls through the gates, unreachable buy
/// markets, too few credits, a full hold, the gate's material, stale and predicted markets, and trade volumes from 6 to 180
/// against holds up to 490, so trips span many batches. On a mismatch the description is written to a file the failure names.
/// </remarks>
public sealed class RoutePlannerGoldenTests
{
    // Taken from the planner as merged in projects#250 (7bcbfe8e), before slice 6.61 touched it.
    private const string Fingerprint = "2BB1A6B5D335C315A04F3DCD04E79B3672F94A05ADA1F8ABA65BCA5163047AE0";

    [Fact]
    public void ThePlannersAnswers_OnAGeneratedWorld_AreThoseOfThePlannerBeforeTheSpeedUps()
    {
        var text = Describe(GeneratedWorld.Map(), GeneratedWorld.Ships());
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        if (fingerprint != Fingerprint)
        {
            var path = Path.Combine(Path.GetTempPath(), $"route-planner-golden-{fingerprint[..12]}.txt");
            File.WriteAllText(path, text);
            fingerprint.Should().Be(Fingerprint, $"the planner's answers must not change; they are written to {path}");
        }
    }

    private static string Describe(TradeMarketMap map, IReadOnlyList<ShipModel> ships)
    {
        var lines = new List<string>();
        foreach (var ship in ships)
        {
            foreach (var credits in new long[] { 6_000, 400_000, 20_000_000 })
            {
                foreach (var minimum in new[] { 40, 200 })
                {
                    var none = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var ranked = TradeRoutePlanner.Rank(map, ship, credits, minimum, none, HeldBuys.None);
                    Add(lines, $"rank {ship.Symbol} {credits} {minimum}", ranked);
                    Add(lines, $"judge {ship.Symbol} {credits} {minimum}", TradeRoutePlanner.Judge(map, ship, credits, minimum, none, HeldBuys.None));

                    // The routes other trips hold are left out (D80's keys): the three best here.
                    var held = ranked.Take(3).Select(route => route.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Add(lines, $"rank-held {ship.Symbol} {credits} {minimum}", TradeRoutePlanner.Rank(map, ship, credits, minimum, held, HeldBuys.None));
                    Add(lines, $"judge-held {ship.Symbol} {credits} {minimum}", TradeRoutePlanner.Judge(map, ship, credits, minimum, held, HeldBuys.None));
                }
            }

            Add(lines, $"rank-in-system {ship.Symbol}", TradeRoutePlanner.Rank(map.WithoutJumps(), ship, 400_000, 40, new HashSet<string>(), HeldBuys.None));
        }

        var probe = GeneratedWorld.Ship("PROBE-RATE", "X1-S2", 3, "DOCKED", fuel: 600, tank: 600, hold: 80);
        foreach (var market in map.MarketWaypoints)
        {
            lines.Add(Invariant($"rate {market} {TradeRoutePlanner.BestRateThrough(map, market, probe with { SystemSymbol = map.SystemOf(market) }):F6}"));
        }

        var markets = map.MarketWaypoints.ToList();
        for (var from = 0; from < markets.Count; from++)
        {
            for (var to = from % 5; to < markets.Count; to += 5)
            {
                foreach (var (fuel, tank) in new[] { (80, 80), (100, 300), (600, 600), (4_000, 4_000) })
                {
                    var found = TradeRoutePlanner.TryPlanFlight(map, markets[from], markets[to], fuel, tank, out var flight);
                    lines.Add(Invariant($"flight {markets[from]}>{markets[to]} {fuel}/{tank} {found} [{string.Join(",", flight.Stops)}] {flight.FuelCost} {flight.FuelLeft} {flight.Jumps.Count} {flight.AntimatterCost}"));
                }
            }
        }

        return string.Join('\n', lines);
    }

    private static void Add<T>(List<string> lines, string heading, IEnumerable<T> answers)
    {
        var all = answers.ToList();
        lines.Add(Invariant($"{heading}: {all.Count}"));
        lines.AddRange(all.Select(answer => "  " + Properties(answer!)));
    }

    /// <summary>Every public property, by name: a route's figures, a judgement's check, ship and route.</summary>
    private static string Properties(object value) => value switch
    {
        null => "null",
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("F6", CultureInfo.InvariantCulture),
        IFormattable formattable when value.GetType().IsPrimitive || value is decimal or Enum => formattable.ToString(null, CultureInfo.InvariantCulture),
        Enum check => check.ToString(),
        ShipModel ship => ship.Symbol,
        System.Collections.IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Properties)) + "]",
        _ => "{" + string.Join(" ", value.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={Properties(property.GetValue(value)!)}")) + "}",
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>A seeded world for <see cref="RoutePlannerGoldenTests"/>: six systems, their gates, markets and ships.</summary>
internal static class GeneratedWorld
{
    /// <summary>When the gates are judged, and the ships' cooldowns are counted from.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 00, 00, TimeSpan.Zero);

    private const int Systems = 6;
    private const int WaypointsPerSystem = 14;

    private static readonly string[] TradeGoods = ["IRON", "COPPER", "ALUMINUM", "PLASTICS", "FABRICS", "ELECTRONICS", "MACHINERY", "EQUIPMENT", "MEDICINE", "FOOD", "FAB_MATS"];
    private static readonly int[] BasePrices = [60, 85, 140, 450, 900, 2_400, 3_100, 3_900, 5_200, 300, 1_800];
    private static readonly int[] Volumes = [6, 20, 40, 60, 180];
    private static readonly string[] Supplies = ["SCARCE", "LIMITED", "MODERATE", "HIGH", "ABUNDANT"];
    private static readonly string[] Types = ["IMPORT", "EXPORT", "EXCHANGE"];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MadeFrom = new Dictionary<string, IReadOnlyList<string>>
    {
        ["PLASTICS"] = ["IRON"],
        ["FAB_MATS"] = ["IRON", "COPPER"],
        ["ELECTRONICS"] = ["COPPER", "PLASTICS"],
        ["MACHINERY"] = ["IRON", "ALUMINUM"],
        ["EQUIPMENT"] = ["ALUMINUM", "PLASTICS"],
        ["FABRICS"] = ["FOOD"],
        ["MEDICINE"] = ["FABRICS"],
    };

    /// <summary>The systems in a chain through their gates, with two shortcuts: S0–S3 and S2–S5.</summary>
    private static readonly (int A, int B)[] Links = [(0, 1), (1, 2), (2, 3), (3, 4), (4, 5), (0, 3), (2, 5)];

    public static string SystemOf(int system) => $"X1-S{system}";

    public static string GateOf(int system) => $"{SystemOf(system)}-G";

    public static string WaypointOf(int system, int waypoint) => $"{SystemOf(system)}-W{waypoint:D2}";

    /// <summary>The map across all six systems, as a reach map is.</summary>
    public static TradeMarketMap Map()
    {
        var random = new Random(6_61);
        var waypoints = new List<WaypointCacheModel>();
        var markets = new List<MarketSnapshot>();
        var stale = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var predicted = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var antimatter = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var system = 0; system < Systems; system++)
        {
            var gate = GateOf(system);
            waypoints.Add(new WaypointCacheModel(gate, SystemOf(system), "JUMP_GATE", random.Next(-300, 301), random.Next(-300, 301), true, false, DateTimeOffset.UnixEpoch));
            var gateFuel = random.Next(60, 120);
            antimatter[gate] = random.Next(4_000, 6_001);
            markets.Add(Market(gate, system, [
                new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", (int)antimatter[gate], (int)antimatter[gate] - 200, 10, "MODERATE"),
                .. system % 2 == 0 ? new[] { new TradeGoodSnapshot("FUEL", "EXCHANGE", gateFuel, gateFuel - 8, 180, "HIGH") } : []]));

            for (var index = 0; index < WaypointsPerSystem; index++)
            {
                var symbol = WaypointOf(system, index);
                var hasMarket = random.NextDouble() < 0.75;
                waypoints.Add(new WaypointCacheModel(symbol, SystemOf(system), "PLANET", random.Next(-450, 451), random.Next(-450, 451), hasMarket, false, DateTimeOffset.UnixEpoch));
                if (!hasMarket)
                {
                    continue;
                }

                var goods = new List<TradeGoodSnapshot>();
                if (random.NextDouble() < 0.5)
                {
                    var fuel = random.Next(55, 130);
                    goods.Add(new TradeGoodSnapshot("FUEL", "EXCHANGE", fuel, fuel - random.Next(3, 12), 180, "MODERATE"));
                }

                foreach (var good in Enumerable.Range(0, TradeGoods.Length).OrderBy(_ => random.Next()).Take(random.Next(2, 7)))
                {
                    var purchase = (int)Math.Round(BasePrices[good] * (0.65 + (random.NextDouble() * 0.8)));
                    var sale = (int)Math.Round(purchase * (0.82 + (random.NextDouble() * 0.15)));
                    goods.Add(new TradeGoodSnapshot(TradeGoods[good], Types[random.Next(Types.Length)], purchase, sale, Volumes[random.Next(Volumes.Length)], Supplies[random.Next(Supplies.Length)]));
                }

                markets.Add(Market(symbol, system, goods));
                var roll = random.NextDouble();
                if (roll < 0.15)
                {
                    stale.Add(symbol);
                }
                else if (roll < 0.45)
                {
                    predicted[symbol] = Math.Round(0.1 + (random.NextDouble() * 0.8), 3);
                }
            }
        }

        // At home, a factory that makes the gate's FAB_MATS from IRON and COPPER: trips that feed the gate's chain (D89, D90).
        waypoints.Add(new WaypointCacheModel("X1-S0-F", SystemOf(0), "MOON", 120, -80, true, false, DateTimeOffset.UnixEpoch));
        markets.Add(Market("X1-S0-F", 0, [
            new TradeGoodSnapshot("IRON", "IMPORT", 95, 78, 60, "SCARCE"),
            new TradeGoodSnapshot("COPPER", "IMPORT", 120, 101, 40, "LIMITED"),
            new TradeGoodSnapshot("FAB_MATS", "EXPORT", 1_500, 1_350, 20, "MODERATE"),
            new TradeGoodSnapshot("FUEL", "EXCHANGE", 90, 82, 180, "MODERATE")]));

        return new TradeMarketMap(waypoints, markets, MadeFrom)
        {
            ConstructionMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "FAB_MATS" },
            ConstructionSystemSymbol = SystemOf(0),
            StaleMarkets = stale,
            PredictionWeights = predicted,
            Gates = new TradeGates(
                Network(),
                Now,
                5,
                antimatter,
                Enumerable.Range(0, Systems).ToDictionary(SystemOf, system => (system * 700, (system % 2) * 500), StringComparer.OrdinalIgnoreCase),
                50_000),
        };
    }

    /// <summary>Six traders: a heavy freighter, a light hauler, a small trader cooling down, a full command ship, a drone, a fast one.</summary>
    public static IReadOnlyList<ShipModel> Ships() =>
    [
        Ship("T-HEAVY", "X1-S0", 1, "DOCKED", fuel: 4_000, tank: 4_000, hold: 490, speed: 10),
        Ship("T-LIGHT", "X1-S1", 3, "IN_ORBIT", fuel: 250, tank: 600, hold: 80, speed: 15),
        Ship("T-SMALL", "X1-S2", -1, "IN_ORBIT", fuel: 300, tank: 300, hold: 40, speed: 30, cooldown: Now.AddSeconds(200)),
        Ship("T-CMD", "X1-S3", 5, "DOCKED", fuel: 400, tank: 400, hold: 40, speed: 30, cargo: 40),
        Ship("T-DRONE", "X1-S4", 2, "DOCKED", fuel: 80, tank: 80, hold: 15, speed: 10),
        Ship("T-FAST", "X1-S5", 7, "IN_ORBIT", fuel: 800, tank: 800, hold: 40, speed: 36),
    ];

    /// <summary>A ship at a waypoint of a system (-1: its gate), with its tank, hold, engine speed, cargo and cooldown.</summary>
    public static ShipModel Ship(string symbol, string system, int waypoint, string status, int fuel, int tank, int hold, int speed = 30, int cargo = 0, DateTimeOffset? cooldown = null)
        => new(
            symbol,
            system,
            waypoint < 0 ? $"{system}-G" : $"{system}-W{waypoint:D2}",
            status,
            "CRUISE",
            fuel,
            tank,
            CargoCurrent: cargo,
            CargoCapacity: hold,
            ShipType: "HAULER",
            MountSymbols: [],
            CargoInventory: cargo > 0 ? [new CargoItemModel("IRON", cargo)] : [],
            EngineJson: string.Create(CultureInfo.InvariantCulture, $$"""{"speed":{{speed}}}"""),
            CooldownExpiresAt: cooldown);

    private static MarketSnapshot Market(string waypoint, int system, IReadOnlyList<TradeGoodSnapshot> goods)
        => new(
            waypoint,
            SystemOf(system),
            goods,
            [.. goods.Where(good => good.Type == "IMPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXCHANGE").Select(good => good.Symbol)]);

    private static ExplorePlanState Network() => new()
    {
        ShipSymbol = "T-FAST",
        HomeSystemSymbol = SystemOf(0),
        Status = ExploreStatus.Done,
        UpdatedAt = Now,
        Systems = [.. Enumerable.Range(0, Systems).Select(system => new KnownSystem
        {
            SystemSymbol = SystemOf(system),
            GateWaypointSymbol = GateOf(system),
            Gate = GateState.Active,
            GateCheckedAt = Now,
            Connections = [.. Links.Where(link => link.A == system || link.B == system).Select(link => GateOf(link.A == system ? link.B : link.A))],
            ConnectionsCheckedAt = Now,
            ExploredAt = Now,
        })],
    };
}

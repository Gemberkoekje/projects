using FluentAssertions;
using SpaceTraders.Application.Exploring;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.31 (D101: "One planner for every way ... Whether to jump or use a warp drive if the ship has one, based on distance
/// and fuel"; D100 fuel-safe; D104 "CRUISE/BURN only"). The systems are as they lay on 2026-10-06: home X1-FJ91 at
/// (20763, 2994), its built gate connected to X1-GT9's at (17833, 3117), 2,933 away; X1-ZZ69 at (17776, 3645), 531 from X1-GT9,
/// its gate still under construction; X1-XJ90 at (20770, 3949), 955 from home, behind an unbuilt gate too. Every market there
/// sold FUEL. The explorer: speed 36, an 800-unit tank, a warp drive of range 2,000.
/// </summary>
public sealed class SystemWaysTests
{
    private const string Home = "X1-FJ91";
    private const string HomeGate = "X1-FJ91-I64";
    private const string Gt9 = "X1-GT9";
    private const string Gt9Gate = "X1-GT9-E10Z";
    private const string Zz69 = "X1-ZZ69";
    private const string Zz69Gate = "X1-ZZ69-I53";

    private static readonly DateTimeOffset Now = new(2026, 10, 06, 14, 00, 00, TimeSpan.Zero);

    private readonly Dictionary<string, (int X, int Y)> _systems = new(StringComparer.OrdinalIgnoreCase)
    {
        [Home] = (20763, 2994),
        [Gt9] = (17833, 3117),
        [Zz69] = (17776, 3645),
        ["X1-XJ90"] = (20770, 3949),
    };

    private readonly List<WayPoint> _waypoints =
    [
        new(HomeGate, Home, 100, 0, Refuels: true),
        new("X1-FJ91-A1", Home, 0, 0, Refuels: true),
        new(Gt9Gate, Gt9, 0, 0, Refuels: true),
        new("X1-GT9-AE7B", Gt9, 40, 30, Refuels: true),
        new("X1-GT9-ROCK", Gt9, 300, 0, Refuels: false),
        new(Zz69Gate, Zz69, 10, -20, Refuels: true),
        new("X1-ZZ69-A1", Zz69, 0, 0, Refuels: true),
        new("X1-ZZ69-ROCK", Zz69, 200, 200, Refuels: false),
        new("X1-XJ90-B6", "X1-XJ90", 0, 0, Refuels: true),
    ];

    private readonly List<KnownSystem> _known =
    [
        new() { SystemSymbol = Home, GateWaypointSymbol = HomeGate, Gate = GateState.Active, Connections = [Gt9Gate], ExploredAt = Now.AddHours(-9) },
        new() { SystemSymbol = Gt9, GateWaypointSymbol = Gt9Gate, Gate = GateState.Active, Connections = [HomeGate], ExploredAt = Now.AddHours(-8) },
        new() { SystemSymbol = Zz69, GateWaypointSymbol = Zz69Gate, Gate = GateState.UnderConstruction },
        new() { SystemSymbol = "X1-XJ90", GateWaypointSymbol = "X1-XJ90-I59", Gate = GateState.UnderConstruction },
    ];

    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void ASystemBehindAGateUnderConstruction_IsReached_ByAJumpAndAWarpFromTheNearestSystemTheGatesReach()
    {
        // No gate leads to X1-ZZ69: the explorer jumps to X1-GT9 and warps the 531 from its gate, whose market fills the tank;
        // 1,062 for BURN is more than the tank, so it cruises, and lands at the market by the gate. A jump's cooldown holds back
        // the next jump only, not a warp.
        var ways = SystemWays.From(Chart(), Explorer(HomeGate));

        var way = ways[Zz69];
        way.Steps.Should().HaveCount(2);
        way.Steps[0].Should().BeEquivalentTo(new { Kind = WayStepKind.Jump, FromWaypointSymbol = HomeGate, ToWaypointSymbol = Gt9Gate });
        way.Steps[1].Should().BeEquivalentTo(new { Kind = WayStepKind.Warp, FromWaypointSymbol = Gt9Gate, ToWaypointSymbol = Zz69Gate, FlightMode = "CRUISE", Fuel = 531, Seconds = 753.0 });
        way.Seconds.Should().Be(763);
        way.Jumps.Should().Be(1);
        way.Warps.Should().Be(1);
    }

    [Fact]
    public void NoWarpGoesFurtherThanTheTankHolds_ForItNeverDrifts()
    {
        // D104: X1-XJ90 lies 955 from home, within the drive's 2,000 but beyond the 800-unit tank, and 1,497 or more from the
        // other systems: no way leads there.
        SystemWays.From(Chart(), Explorer(HomeGate)).Should().NotContainKey("X1-XJ90");
    }

    [Fact]
    public void WhereAJumpIsFaster_TheWayJumps()
    {
        // X1-NEAR, 500 from home, is a jump from home's gate: no time but the stop, where the warp takes 709 seconds.
        AddSystem("X1-NEAR", (21063, 3394), gateConnectedFrom: Home, refuels: true);

        var way = SystemWays.From(Chart(), Explorer(HomeGate))["X1-NEAR"];

        way.Steps.Should().ContainSingle().Which.Kind.Should().Be(WayStepKind.Jump);
    }

    [Fact]
    public void WhereTheWarpIsFaster_TheWayWarps()
    {
        // X1-NEAR lies 300 from home, but its gate connects only to X1-FAR's, 2,000 from both: two jumps, the second after a
        // cooldown of 17 + 0.311 × 2,000 = 639 seconds. The warp burns: 600 fuel and 223 seconds.
        AddSystem("X1-FAR", (22763, 2994), gateConnectedFrom: Home, refuels: true, explored: true);
        AddSystem("X1-NEAR", (20763, 3294), gateConnectedFrom: "X1-FAR", refuels: true);

        var way = SystemWays.From(Chart(), Explorer(HomeGate))["X1-NEAR"];

        way.Steps.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Kind = WayStepKind.Warp, FlightMode = "BURN", Fuel = 600, Seconds = 223.0 });
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(450, false)]
    public void IntoASystemWithNowhereToRefuel_AWarpKeepsTheFuelToWarpBack_AndGoesNoFurther(int distance, bool reached)
    {
        // D100: "a warp only where the ship can refuel, or has the fuel to warp back". X1-BARE has no market: 300 there and 300
        // back fit the tank, in CRUISE; 450 and 450 don't. X1-BEYOND, 400 past it and 1,000 from home, isn't reached through it.
        AddSystem("X1-BARE", (20763, 2994 + distance), gateConnectedFrom: null, refuels: false);
        AddSystem("X1-BEYOND", (20763, 3994), gateConnectedFrom: null, refuels: true);

        var ways = SystemWays.From(Chart(), Explorer(HomeGate));

        ways.ContainsKey("X1-BARE").Should().Be(reached);
        if (reached)
        {
            ways["X1-BARE"].Steps.Should().ContainSingle().Which.FlightMode.Should().Be("CRUISE");
        }

        ways.Should().NotContainKey("X1-BEYOND");
    }

    [Fact]
    public void ASystemTheApiRefusedAWarpInto_GetsNoneForAnHour()
    {
        _refused.Add(Zz69);

        SystemWays.From(Chart(), Explorer(HomeGate)).Should().NotContainKey(Zz69);
    }

    [Fact]
    public void AShipWithoutAWarpDrive_OnlyJumps()
    {
        var ways = SystemWays.From(Chart(), Explorer(HomeGate) with { WarpRange = 0 });

        ways.Keys.Should().BeEquivalentTo([Gt9]);
    }

    [Fact]
    public void ShortOfFuelWhereItCantRefuel_TheWarpLeavesFromItsSystemsNearestMarket()
    {
        // At X1-GT9's asteroid with 100 fuel: it flies to the nearest market first, AE7B, 262 away (197 seconds at 36, and a
        // stop to refuel), and warps from there.
        var ways = SystemWays.From(Chart(), new WayShip(Gt9, "X1-GT9-ROCK", 100, 800, 36, 2000, 0));

        var warp = ways[Zz69].Steps.Should().ContainSingle().Subject;
        warp.FromWaypointSymbol.Should().Be("X1-GT9-AE7B");
        warp.Seconds.Should().BeApproximately(15 + (262 * 25.0 / 36) + 10 + 753, 0.001);
    }

    [Fact]
    public void ToAWaypoint_TheLastWarpLandsThere_WhereTheShipCanRefuel()
    {
        SystemWays.TryFind(Chart(), Explorer(Gt9Gate, Gt9), "X1-ZZ69-A1", out var toMarket).Should().BeTrue();
        toMarket.Steps[^1].ToWaypointSymbol.Should().Be("X1-ZZ69-A1");

        SystemWays.TryFind(Chart(), Explorer(Gt9Gate, Gt9), "X1-ZZ69-ROCK", out var toAsteroid).Should().BeTrue();
        toAsteroid.Steps[^1].ToWaypointSymbol.Should().Be(Zz69Gate, "it lands where it can refuel, and flies on from there");
    }

    private static WayShip Explorer(string waypoint, string system = Home) => new(system, waypoint, 800, 800, 36, 2000, 0);

    private WayChart Chart()
        => new(
            new ExplorePlanState { ShipSymbol = "SPECTER-1", HomeSystemSymbol = Home, Status = ExploreStatus.Done, UpdatedAt = Now, Systems = _known },
            Now,
            _systems,
            _waypoints,
            _refused);

    /// <summary>
    /// A system at <paramref name="position"/> with a gate (a market, where <paramref name="refuels"/>) and a planet; its gate
    /// connected from <paramref name="gateConnectedFrom"/>'s, or to none.
    /// </summary>
    private void AddSystem(string system, (int X, int Y) position, string? gateConnectedFrom, bool refuels, bool explored = false)
    {
        var gate = $"{system}-G";
        _systems[system] = position;
        _waypoints.Add(new WayPoint(gate, system, 0, 0, refuels));
        _waypoints.Add(new WayPoint($"{system}-P", system, 50, 0, refuels));
        if (gateConnectedFrom is not null)
        {
            var from = _known.FindIndex(known => known.SystemSymbol == gateConnectedFrom);
            _known[from] = _known[from] with { Connections = [.. _known[from].Connections ?? [], gate] };
        }

        _known.Add(new KnownSystem
        {
            SystemSymbol = system,
            GateWaypointSymbol = gate,
            Gate = gateConnectedFrom is null ? GateState.UnderConstruction : GateState.Active,
            Connections = explored ? [] : null,
            ExploredAt = explored ? Now.AddHours(-1) : null,
        });
    }
}

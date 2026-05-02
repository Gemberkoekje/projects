using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Services;

public sealed class AssignmentResolverTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ShipModel MakeShip(string symbol = "SHIP-1", string system = "X1-AB", string waypoint = "X1-AB-STATION")
        => new(symbol, system, waypoint, "IN_ORBIT", "CRUISE", 80, 100, CargoCurrent: 0, CargoCapacity: 40);

    private static WaypointCacheModel MakeWaypoint(string symbol, string system, string type, int x, int y, string? traitsJson = null)
        => new(symbol, system, type, x, y, false, false, DateTimeOffset.UtcNow, TraitsJson: traitsJson);

    private static WaypointCacheModel MakeMarket(string symbol, string system, int x, int y)
        => new(symbol, system, "PLANET", x, y, true, false, DateTimeOffset.UtcNow);

    private static MarketSnapshot MakeMarketSnapshot(string waypointSymbol, string system, IReadOnlyList<string>? imports = null, IReadOnlyList<string>? exchange = null)
        => new(waypointSymbol, system, [], imports ?? [], [], exchange ?? []);

    private static ResourceProductionGoal MiningGoal(string tradeSymbol)
        => new(tradeSymbol, 100, ResourceProductionPurpose.Market, 1);

    private static AssignmentResolver Build(IWaypointRepository waypoints, IMarketRepository markets)
        => new(waypoints, markets);

    // -------------------------------------------------------------------------
    // ResolveAsync: asteroid selection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_AsteroidWithMatchingTrait_ReturnsAsteroidWithTrait()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        // Two asteroids in the system: one far with no trait, one close with matching trait.
        var asteroidNear = MakeWaypoint("X1-AB-AST1", "X1-AB", "ASTEROID", 5, 0, traitsJson: "IRON_ORE");
        var asteroidFar = MakeWaypoint("X1-AB-AST2", "X1-AB", "ASTEROID", 100, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([asteroidNear, asteroidFar]);

        var ship = MakeShip();
        var goal = MiningGoal("IRON_ORE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeOfType<MineResourceGoal>();
        var mineGoal = (MineResourceGoal)result!;
        mineGoal.TradeSymbol.Should().Be("IRON_ORE");
        // The asteroid with the matching trait should be selected even though it is farther away.
        mineGoal.SourceWaypointSymbol.Should().Be("X1-AB-AST1");
    }

    [Fact]
    public async Task ResolveAsync_NoAsteroidWithTrait_ReturnsNearestAsteroid()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        // Ship is at origin; nearest asteroid is AST1.
        var asteroidNear = MakeWaypoint("X1-AB-AST1", "X1-AB", "ASTEROID", 10, 0);
        var asteroidFar = MakeWaypoint("X1-AB-AST2", "X1-AB", "ASTEROID", 50, 0);
        // Station where the ship is located.
        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([station, asteroidNear, asteroidFar]);

        var ship = MakeShip();
        var goal = MiningGoal("BAUXITE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeOfType<MineResourceGoal>();
        var mineGoal = (MineResourceGoal)result!;
        mineGoal.SourceWaypointSymbol.Should().Be("X1-AB-AST1",
            because: "no trait matches so the nearest asteroid should be chosen");
    }

    [Fact]
    public async Task ResolveAsync_TraitMatchTakesPriorityOverDistance()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        // AST-NEAR is closer but has no trait; AST-FAR is farther but has the matching trait.
        var asteroidNear = MakeWaypoint("X1-AB-AST-NEAR", "X1-AB", "ASTEROID_FIELD", 1, 0);
        var asteroidFar = MakeWaypoint("X1-AB-AST-FAR", "X1-AB", "ASTEROID_FIELD", 80, 0, traitsJson: "BAUXITE");
        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([station, asteroidNear, asteroidFar]);

        var ship = MakeShip();
        var goal = MiningGoal("BAUXITE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeOfType<MineResourceGoal>();
        ((MineResourceGoal)result!).SourceWaypointSymbol.Should().Be("X1-AB-AST-FAR");
    }

    // -------------------------------------------------------------------------
    // ResolveAsync: gas siphon
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_HydrocarbonGoal_ReturnsNearestGasGiant()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var gasGiantNear = MakeWaypoint("X1-AB-GG1", "X1-AB", "GAS_GIANT", 20, 0);
        var gasGiantFar = MakeWaypoint("X1-AB-GG2", "X1-AB", "GAS_GIANT", 90, 0);
        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([station, gasGiantNear, gasGiantFar]);

        var ship = MakeShip();
        var goal = MiningGoal("HYDROCARBON");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeOfType<SiphonResourceGoal>();
        var siphonGoal = (SiphonResourceGoal)result!;
        siphonGoal.TradeSymbol.Should().Be("HYDROCARBON");
        siphonGoal.SourceWaypointSymbol.Should().Be("X1-AB-GG1");
    }

    // -------------------------------------------------------------------------
    // ResolveAsync: fallback / unresolvable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_NoWaypointsCached_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var ship = MakeShip();
        var goal = MiningGoal("IRON_ORE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeNull(because: "no cached waypoints means the assignment is unresolvable");
    }

    [Fact]
    public async Task ResolveAsync_NoAsteroidsInSystem_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var planet = MakeWaypoint("X1-AB-PLANET", "X1-AB", "PLANET", 10, 0);
        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([planet, station]);

        var ship = MakeShip();
        var goal = MiningGoal("IRON_ORE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeNull(because: "no asteroids means mining is unresolvable in this system");
    }

    [Fact]
    public async Task ResolveAsync_HydrocarbonButNoGasGiant_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var asteroid = MakeWaypoint("X1-AB-AST", "X1-AB", "ASTEROID", 10, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([asteroid]);

        var ship = MakeShip();
        var goal = MiningGoal("HYDROCARBON");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeNull(because: "no gas giant means siphoning HYDROCARBON is unresolvable");
    }

    [Fact]
    public async Task ResolveAsync_ShipHasNoSystemSymbol_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var ship = new ShipModel("SHIP-1", null, null, "IN_ORBIT", "CRUISE", 80, 100);
        var goal = MiningGoal("IRON_ORE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().BeNull();
        await waypoints.DidNotReceive().GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // FindNearestSellMarketAsync: market sell resolution
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindNearestSellMarketAsync_MarketImportsSymbol_ReturnsNearestMarket()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);
        var marketNear = MakeMarket("X1-AB-MARKET-NEAR", "X1-AB", 15, 0);
        var marketFar = MakeMarket("X1-AB-MARKET-FAR", "X1-AB", 60, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([station, marketNear, marketFar]);

        var snapshotNear = MakeMarketSnapshot("X1-AB-MARKET-NEAR", "X1-AB", imports: ["IRON_ORE"]);
        var snapshotFar = MakeMarketSnapshot("X1-AB-MARKET-FAR", "X1-AB", imports: ["IRON_ORE"]);

        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([snapshotNear, snapshotFar]);

        var ship = MakeShip();

        var resolver = Build(waypoints, markets);
        var result = await resolver.FindNearestSellMarketAsync(ship, "IRON_ORE");

        result.Should().Be("X1-AB-MARKET-NEAR");
    }

    [Fact]
    public async Task FindNearestSellMarketAsync_MarketExchangesSymbol_IsIncluded()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var market = MakeMarket("X1-AB-EXCHANGE", "X1-AB", 10, 0);
        var station = MakeWaypoint("X1-AB-STATION", "X1-AB", "ORBITAL_STATION", 0, 0);

        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([station, market]);

        var snapshot = MakeMarketSnapshot("X1-AB-EXCHANGE", "X1-AB", exchange: ["COPPER_ORE"]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([snapshot]);

        var ship = MakeShip();

        var resolver = Build(waypoints, markets);
        var result = await resolver.FindNearestSellMarketAsync(ship, "COPPER_ORE");

        result.Should().Be("X1-AB-EXCHANGE");
    }

    [Fact]
    public async Task FindNearestSellMarketAsync_NoMarketForSymbol_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var market = MakeMarket("X1-AB-MARKET", "X1-AB", 10, 0);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([market]);

        // Market only imports something else.
        var snapshot = MakeMarketSnapshot("X1-AB-MARKET", "X1-AB", imports: ["FUEL"]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([snapshot]);

        var ship = MakeShip();

        var resolver = Build(waypoints, markets);
        var result = await resolver.FindNearestSellMarketAsync(ship, "IRON_ORE");

        result.Should().BeNull(because: "no market in the system imports or exchanges IRON_ORE");
    }

    [Fact]
    public async Task FindNearestSellMarketAsync_NoSnapshotsCached_ReturnsNull()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var asteroid = MakeWaypoint("X1-AB-AST", "X1-AB", "ASTEROID", 10, 0);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([asteroid]);

        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([]);

        var ship = MakeShip();

        var resolver = Build(waypoints, markets);
        var result = await resolver.FindNearestSellMarketAsync(ship, "IRON_ORE");

        result.Should().BeNull(because: "no market snapshots are cached");
    }

    // -------------------------------------------------------------------------
    // ResolveAsync: goal metadata is preserved
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveAsync_MiningGoal_GoalIdIsAssigned()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var asteroid = MakeWaypoint("X1-AB-AST", "X1-AB", "ASTEROID", 10, 0);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([asteroid]);

        var ship = MakeShip();
        var goal = MiningGoal("IRON_ORE");

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveAsync(ship, goal);

        result.Should().NotBeNull();
        result!.GoalId.Should().NotBe(Guid.Empty);
    }

    // -------------------------------------------------------------------------
    // ResolveMarketCoverageAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResolveMarketCoverageAsync_WhenNoSnapshot_ReturnsScoutWaypointGoal()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        markets.FindSnapshotByWaypointAsync("X1-AB-UNKNOWN", Arg.Any<CancellationToken>())
            .Returns((MarketSnapshot?)null);

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveMarketCoverageAsync("X1-AB-UNKNOWN");

        result.Should().BeOfType<ScoutWaypointGoal>()
            .Which.TargetWaypointSymbol.Should().Be("X1-AB-UNKNOWN");
    }

    [Fact]
    public async Task ResolveMarketCoverageAsync_WhenSnapshotExists_ReturnsPatrolMarketGoal()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var snapshot = MakeMarketSnapshot("X1-AB-MKT", "X1-AB");
        markets.FindSnapshotByWaypointAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveMarketCoverageAsync("X1-AB-MKT");

        result.Should().BeOfType<PatrolMarketGoal>()
            .Which.TargetWaypointSymbol.Should().Be("X1-AB-MKT");
    }

    [Fact]
    public async Task ResolveMarketCoverageAsync_GoalIdIsAssigned()
    {
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        markets.FindSnapshotByWaypointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MarketSnapshot?)null);

        var resolver = Build(waypoints, markets);
        var result = await resolver.ResolveMarketCoverageAsync("X1-AB-MKT");

        result.GoalId.Should().NotBe(Guid.Empty);
    }
}

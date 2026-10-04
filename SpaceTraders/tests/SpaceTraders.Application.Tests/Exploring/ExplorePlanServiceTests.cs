using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Asked on 2026-10-04: "if an active jump gate goes to a system that isn't explored yet, the COMMAND ship should go through
/// that jump gate. If there are markets or shipyard there, the COMMAND ship should scout them, as it initially does for the
/// home system, recursively." The gates are those of 2026-10-04 (public API): X1-DC53-I55, built, connects to X1-KR90 and
/// X1-MT49, whose gates are built, and to X1-HZ59 and X1-BG54, whose gates are still under construction. The plan runs pass
/// after pass, as the tick runs it, over the cache the real repositories keep; the API is a fake.
/// </summary>
public sealed class ExplorePlanServiceTests
{
    private const string Ship = "SPECTER-1";
    private const string Home = "X1-DC53";
    private const string HomeGate = "X1-DC53-I55";
    private const string Kr90 = "X1-KR90";
    private const string Kr90Gate = "X1-KR90-AF5F";
    private const string Mt49 = "X1-MT49";
    private const string Mt49Gate = "X1-MT49-DX8X";
    private const string Hz59Gate = "X1-HZ59-I59";
    private const string Bg54Gate = "X1-BG54-I54";

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly LogRecorder _log = new();

    public ExplorePlanServiceTests()
    {
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);

        // The gate as the API shows it now: built. The cache still has it as startup sync saw it when the agent started.
        _port.GetWaypointAsync(Home, HomeGate, Arg.Any<CancellationToken>()).Returns(Gate(HomeGate, built: true));
        _port.GetJumpGateConnectionsAsync(Home, HomeGate, Arg.Any<CancellationToken>())
            .Returns(new JumpGateConnectionModel(HomeGate, [Hz59Gate, Bg54Gate, Kr90Gate, Mt49Gate]));
        _port.GetWaypointAsync("X1-HZ59", Hz59Gate, Arg.Any<CancellationToken>()).Returns(Gate(Hz59Gate, built: false));
        _port.GetWaypointAsync("X1-BG54", Bg54Gate, Arg.Any<CancellationToken>()).Returns(Gate(Bg54Gate, built: false));
        _port.GetWaypointAsync(Kr90, Kr90Gate, Arg.Any<CancellationToken>()).Returns(Gate(Kr90Gate, built: true));
        _port.GetWaypointAsync(Mt49, Mt49Gate, Arg.Any<CancellationToken>()).Returns(Gate(Mt49Gate, built: true));

        // X1-KR90: its gate and two markets, a shipyard and an asteroid; it connects back home only.
        _port.GetSystemAsync(Kr90, Arg.Any<CancellationToken>()).Returns(new SystemDataModel(Kr90, "X1", "YOUNG_STAR", 30642, 6351));
        _port.GetWaypointsAsync(Kr90, 1, 20, Arg.Any<CancellationToken>()).Returns(new PagedResult<WaypointDataModel>(
            [
                new WaypointDataModel(Kr90Gate, Kr90, "JUMP_GATE", 100, 0, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]"""),
                new WaypointDataModel("X1-KR90-FAR", Kr90, "PLANET", -300, 0, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]"""),
                new WaypointDataModel("X1-KR90-NEAR", Kr90, "FUEL_STATION", 60, 0, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]"""),
                new WaypointDataModel("X1-KR90-YARD", Kr90, "ORBITAL_STATION", 0, 0, HasMarket: false, HasShipyard: true, TraitsJson: """[{"symbol":"SHIPYARD"}]"""),
                new WaypointDataModel("X1-KR90-ROCK", Kr90, "ASTEROID", 80, 20, HasMarket: false, HasShipyard: false, TraitsJson: """[{"symbol":"COMMON_METAL_DEPOSITS"}]"""),
            ],
            5,
            1,
            20));
        _port.GetJumpGateConnectionsAsync(Kr90, Kr90Gate, Arg.Any<CancellationToken>()).Returns(new JumpGateConnectionModel(Kr90Gate, [HomeGate]));

        // X1-MT49, here without a market or a shipyard: explored as soon as the ship is there. It connects back home only.
        _port.GetSystemAsync(Mt49, Arg.Any<CancellationToken>()).Returns(new SystemDataModel(Mt49, "X1", "RED_STAR", 27670, 6079));
        _port.GetWaypointsAsync(Mt49, 1, 20, Arg.Any<CancellationToken>()).Returns(new PagedResult<WaypointDataModel>(
            [
                new WaypointDataModel(Mt49Gate, Mt49, "JUMP_GATE", 0, 0, HasMarket: false, HasShipyard: false, TraitsJson: "[]"),
                new WaypointDataModel("X1-MT49-ROCK", Mt49, "ASTEROID", 10, 10, HasMarket: false, HasShipyard: false, TraitsJson: "[]"),
            ],
            2,
            1,
            20));
        _port.GetJumpGateConnectionsAsync(Mt49, Mt49Gate, Arg.Any<CancellationToken>()).Returns(new JumpGateConnectionModel(Mt49Gate, [HomeGate]));
    }

    [Fact]
    public async Task OnceItsTripHasEnded_TheCommandShipJumpsTowardsTheNearestSystemNotExploredYet()
    {
        await SeedHomeAsync();

        await PassesAsync(10);

        var goal = await GoalAsync();
        goal.Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = Kr90Gate }, "X1-KR90 comes first by symbol, and the gates of X1-BG54 and X1-HZ59 aren't built");
        (await AssignmentAsync()).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });
        var state = await StateAsync();
        state.Status.Should().Be(ExploreStatus.Exploring);
        state.TargetSystemSymbol.Should().Be(Kr90);
        state.Systems.Select(system => (system.SystemSymbol, system.Gate)).Should().BeEquivalentTo(
        [
            (Home, GateState.Active),
            ("X1-HZ59", GateState.UnderConstruction),
            ("X1-BG54", GateState.UnderConstruction),
            (Kr90, GateState.Active),
            (Mt49, GateState.Active),
        ]);
    }

    [Fact]
    public async Task ItLooksAtTheApi_OnceAPass()
    {
        await SeedHomeAsync();

        for (var pass = 1; pass <= 6; pass++)
        {
            _port.ClearReceivedCalls();
            await PassAsync();

            var reads = _port.ReceivedCalls().Count(call => call.GetMethodInfo().Name is nameof(ISpaceTradersPort.GetWaypointAsync) or nameof(ISpaceTradersPort.GetJumpGateConnectionsAsync));
            reads.Should().Be(1, "pass {0} looks at one gate, or asks one gate for its connections (D19)", pass);
        }

        _port.ClearReceivedCalls();
        await PassAsync();
        _port.ReceivedCalls().Should().BeEmpty("everything the choice needs is known");
    }

    [Fact]
    public async Task AShipOnATrip_IsLeftAloneUntilTheTripEnds()
    {
        await SeedHomeAsync();
        var trip = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-DC53-A1", SellWaypointSymbol = "X1-DC53-H52" };
        await SetGoalAsync(trip);

        await PassesAsync(10);

        (await GoalAsync()).Should().BeOfType<TradeBetweenMarketsGoal>();
        (await AssignmentAsync()).Should().BeNull();
        (await StateAsync()).Status.Should().Be(ExploreStatus.Waiting);

        await ClearGoalAsync();
        await PassAsync();

        (await GoalAsync()).Should().BeOfType<JumpGoal>();
    }

    [Fact]
    public async Task WhileTheScoutPlanHasTheShip_ItWaits()
    {
        await SeedHomeAsync();
        await using (var db = TestDbContextFactory.Create(_database))
        {
            await new ShipAssignmentRepository(db).UpsertAsync(ShipAssignmentDto.CreateScout(Ship, "X1-DC53-A1", 3, DateTimeOffset.UtcNow));
        }

        await PassesAsync(10);

        (await GoalAsync()).Should().BeNull();
        (await AssignmentAsync())!.AssignmentType.Should().Be("Scout");
    }

    [Fact]
    public async Task ExploresEverySystemTheActiveGatesReach_ThenComesHome()
    {
        await SeedHomeAsync();
        await PassesAsync(10);

        // The jump to X1-KR90; there it fetches the system and scouts each market and shipyard once, nearest first from the
        // gate it jumped to. The asteroid has neither.
        await JumpAsync(Kr90, Kr90Gate);
        await PassAsync();

        await _port.Received(1).GetWaypointsAsync(Kr90, 1, 20, Arg.Any<CancellationToken>());
        (await GoalAsync()).Should().BeEquivalentTo(new
        {
            SystemSymbol = Kr90,
            Stops = new[] { Kr90Gate, "X1-KR90-NEAR", "X1-KR90-YARD", "X1-KR90-FAR" },
            Visited = 0,
        });

        // Its scouting done, X1-KR90 is explored, and X1-MT49 is next: back through home, two jumps away.
        await ClearGoalAsync();
        await PassesAsync(3);

        var state = await StateAsync();
        state.Systems.Single(system => system.SystemSymbol == Kr90).ExploredAt.Should().NotBeNull();
        (await GoalAsync()).Should().BeEquivalentTo(new { GateWaypointSymbol = Kr90Gate, DestinationGateWaypointSymbol = HomeGate });
        state.TargetSystemSymbol.Should().Be(Mt49);

        await JumpAsync(Home, HomeGate);
        await PassAsync();
        (await GoalAsync()).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = Mt49Gate });

        // X1-MT49 has no market and no shipyard: explored as soon as the ship is there. Nothing else is in reach: home.
        await JumpAsync(Mt49, Mt49Gate);
        await PassesAsync(3);

        state = await StateAsync();
        state.Systems.Single(system => system.SystemSymbol == Mt49).ExploredAt.Should().NotBeNull();
        state.Status.Should().Be(ExploreStatus.Returning);
        (await GoalAsync()).Should().BeEquivalentTo(new { GateWaypointSymbol = Mt49Gate, DestinationGateWaypointSymbol = HomeGate });

        // Home: the ship is free for the other plans again.
        await JumpAsync(Home, HomeGate);
        await PassAsync();

        (await GoalAsync()).Should().BeNull();
        (await AssignmentAsync())!.CompletedAt.Should().NotBeNull();
        (await StateAsync()).Status.Should().Be(ExploreStatus.Done);

        await PassesAsync(3);
        (await AssignmentAsync())!.CompletedAt.Should().NotBeNull("nothing is left to explore");
    }

    [Fact]
    public async Task AJumpTheApiRefused_LeavesThatGateAloneAndChoosesAgain()
    {
        await SeedHomeAsync();
        await PassesAsync(10);
        var refused = (JumpGoal)(await GoalAsync())!;
        await SetGoalAsync(refused with { Status = GoalStatus.Blocked, StatusReason = JumpGoalExecutor.RefusedReason });

        await PassAsync();

        (await GoalAsync()).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = Mt49Gate, Status = GoalStatus.Assigned });
        (await StateAsync()).Systems.Single(system => system.SystemSymbol == Kr90).JumpRefusedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UntilAJumpLeavesTheCreditFloor_TheShipKeepsItsOtherWork_AndThePlanSaysWhyOnce()
    {
        // Your decision of 2026-10-04: a jump keeps FleetExpansion.MinCreditReserve (60,000). The antimatter costs 4,520 at
        // home's gate: 64,000 credits would leave 59,480.
        await SeedHomeAsync();
        await using (var db = TestDbContextFactory.Create(_database))
        {
            await new MarketRepository(db).UpsertAsync(new MarketDataModel(
                HomeGate,
                Home,
                """[{"symbol":"ANTIMATTER","type":"EXCHANGE","purchasePrice":4520,"sellPrice":4300,"tradeVolume":10,"supply":"MODERATE"}]""",
                "[]",
                "[]",
                """[{"symbol":"ANTIMATTER"}]"""));
        }

        await CreditsAsync(64_000);
        await PassesAsync(10);

        (await GoalAsync()).Should().BeNull();
        (await AssignmentAsync()).Should().BeNull("a free command ship keeps its other work while the credits are short");
        (await StateAsync()).Reason.Should().Be("waiting_for_credits");
        _log.Journal.Where(line => line.EventKind == JournalEvents.PlanBlocked).Should().ContainSingle()
            .Which.Message.Should().Contain("4520").And.Contain("60000").And.Contain("64000");

        await CreditsAsync(64_520);
        await PassAsync();

        (await GoalAsync()).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = Kr90Gate });
        (await StateAsync()).Reason.Should().BeEmpty();
    }

    [Fact]
    public async Task AJumpTheCircuitBreakerBlocked_WaitsForSomeoneToLookAtIt()
    {
        await SeedHomeAsync();
        await PassesAsync(10);
        var jump = (JumpGoal)(await GoalAsync())!;
        await SetGoalAsync(jump with { Status = GoalStatus.Blocked, StatusReason = "runaway" });

        await PassesAsync(3);

        (await GoalAsync()).Should().BeEquivalentTo(new { DestinationGateWaypointSymbol = Kr90Gate, Status = GoalStatus.Blocked, StatusReason = "runaway" });
    }

    private static WaypointDataModel Gate(string symbol, bool built)
        => new(symbol, WaypointSymbols.SystemOf(symbol), "JUMP_GATE", 0, 0, HasMarket: true, HasShipyard: false, TraitsJson: """[{"symbol":"MARKETPLACE"}]""", IsUnderConstruction: !built);

    /// <summary>The agent, its command ship docked at home with nothing to do, and home's waypoints as startup sync cached them.</summary>
    private async Task SeedHomeAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new AgentRepository(db).UpsertAsync(new AgentModel("SPECTER", "account", "X1-DC53-A1", 200_000, "COSMIC", 21));
        await new ShipRepository(db).UpsertAsync(new ShipModel(Ship, Home, "X1-DC53-H52", "DOCKED", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: ExplorePlanService.CommandShipType));
        var seen = DateTimeOffset.UtcNow.AddHours(-12);
        await new WaypointRepository(db).UpsertRangeAsync(
        [
            new WaypointCacheModel("X1-DC53-A1", Home, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, seen, """[{"symbol":"MARKETPLACE"}]"""),
            new WaypointCacheModel("X1-DC53-H52", Home, "MOON", 20, 0, HasMarket: true, HasShipyard: true, seen, """[{"symbol":"MARKETPLACE"},{"symbol":"SHIPYARD"}]"""),
            new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 272, -358, HasMarket: true, HasShipyard: false, seen, """[{"symbol":"MARKETPLACE"}]""", IsUnderConstruction: true),
        ]);
    }

    private async Task CreditsAsync(long credits)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new AgentRepository(db).UpsertAsync(new AgentModel("SPECTER", "account", "X1-DC53-A1", credits, "COSMIC", 21));
    }

    private async Task PassAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ExplorePlanService(
                new AgentRepository(db),
                new ShipRepository(db),
                new ShipGoalRepository(db),
                new ShipAssignmentRepository(db),
                new WaypointRepository(db),
                new SystemRepository(db),
                new MarketRepository(db),
                new ShipyardRepository(db),
                new PlanRepository(db),
                _settings,
                _port,
                _log.For<ExplorePlanService>())
            .EnsureBootstrappedAsync(CancellationToken.None);
    }

    private async Task PassesAsync(int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            await PassAsync();
        }
    }

    /// <summary>What the jump executor does: the ship is in orbit at the gate it jumped to, and its goal has ended.</summary>
    private async Task JumpAsync(string system, string gate)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipRepository(db).UpdateNavAsync(Ship, new NavModel("IN_ORBIT", system, gate, "CRUISE", gate, DateTimeOffset.UtcNow.AddSeconds(-1)), null);
        await new ShipGoalRepository(db).ClearActiveGoalAsync(Ship);
    }

    private async Task<ShipGoal?> GoalAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        return await new ShipGoalRepository(db).GetActiveGoalAsync(Ship);
    }

    private async Task SetGoalAsync(ShipGoal goal)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipGoalRepository(db).SetActiveGoalAsync(Ship, goal);
    }

    private async Task ClearGoalAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipGoalRepository(db).ClearActiveGoalAsync(Ship);
    }

    private async Task<ShipAssignmentDto?> AssignmentAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        return await new ShipAssignmentRepository(db).FindAsync(Ship);
    }

    private async Task<ExplorePlanState> StateAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        return (await new PlanRepository(db).GetAsync<ExplorePlanState>(PlanTypes.Explore))!;
    }
}

using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.31: the explorer warps (D100: "Research how the warp works exactly, then measure, then fuel-safe."; D101 "One planner
/// for every way"), and asked on 2026-10-06: "CRUISE/BURN only" (D104), "Scan when none left" (D105), "Reach first, then nearest"
/// (D106) and "No, gates only" (D107). The systems are as the explore plan knew them that day, shortened: home X1-FJ91, whose
/// built gate connects to X1-GT9's, 2,933 away; X1-GT9's gate connects to X1-ZZ69's too, still under construction, 531 from
/// X1-GT9. The explorer is as X1-GT9-AE7B sold it: speed 36, an 800-unit tank, a warp drive of range 2,000, a sensor array. The
/// plan runs pass after pass over the cache the real repositories keep; the API, the purchases and the order are fakes.
/// </summary>
public sealed class WarpExplorersTests
{
    private const string CommandShip = "SPECTER-1";
    private const string Explorer = "SPECTER-50";
    private const string Home = "X1-FJ91";
    private const string HomeGate = "X1-FJ91-I64";
    private const string Gt9 = "X1-GT9";
    private const string Gt9Gate = "X1-GT9-E10Z";
    private const string Zz69 = "X1-ZZ69";
    private const string Zz69Gate = "X1-ZZ69-I53";
    private const string Market = """[{"symbol":"MARKETPLACE"}]""";

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly IPurchaseOrder _order = Substitute.For<IPurchaseOrder>();
    private readonly WarpRefusals _warpRefusals = new();
    private readonly LogRecorder _log = new();

    public WarpExplorersTests()
    {
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        _settings.GetAsync<int>(ExplorePlanService.SystemsPerExplorerSetting, Arg.Any<CancellationToken>()).Returns(10);
        _settings.GetAsync<int>(ExplorePlanService.MaxExplorersSetting, Arg.Any<CancellationToken>()).Returns(5);

        // X1-ZZ69 as the API shows it: charted by others, its gate a market under construction, a planet, a fuel station and an
        // asteroid.
        _port.GetSystemAsync(Zz69, Arg.Any<CancellationToken>()).Returns(new SystemDataModel(Zz69, "X1", "ORANGE_STAR", 17776, 3645));
        _port.GetWaypointsAsync(Zz69, 1, 20, Arg.Any<CancellationToken>()).Returns(new PagedResult<WaypointDataModel>(
            [
                new WaypointDataModel(Zz69Gate, Zz69, "JUMP_GATE", 10, -20, HasMarket: true, HasShipyard: false, TraitsJson: Market, IsUnderConstruction: true),
                new WaypointDataModel("X1-ZZ69-A1", Zz69, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, TraitsJson: Market),
                new WaypointDataModel("X1-ZZ69-B6", Zz69, "FUEL_STATION", 100, 100, HasMarket: true, HasShipyard: false, TraitsJson: Market),
                new WaypointDataModel("X1-ZZ69-ROCK", Zz69, "ASTEROID", 200, 200, HasMarket: false, HasShipyard: false, TraitsJson: "[]"),
            ],
            4,
            1,
            20));
    }

    [Fact]
    public async Task ASystemBehindAGateUnderConstruction_HasItsWaypointsFetched_AndTheExplorerWarpsThere()
    {
        // D100, D101: the gates don't reach X1-ZZ69; a warp does, 531 from X1-GT9. Its markets are fetched first, so the warp
        // lands where the explorer can refuel: the market at its gate.
        await SeedAsync();

        await PassAsync();

        await _port.Received(1).GetWaypointsAsync(Zz69, 1, 20, Arg.Any<CancellationToken>());
        (await GoalAsync(Explorer)).Should().BeOfType<WarpGoal>().Which.DestinationWaypointSymbol.Should().Be(Zz69Gate);
        (await AssignmentAsync(Explorer)).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });
        (await StateAsync()).Explorers.Should().ContainSingle().Which.Should().BeEquivalentTo(new ExploringShip
        {
            ShipSymbol = Explorer,
            Status = ExploreStatus.Exploring,
            TargetSystemSymbol = Zz69,
        });
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.PlanStarted)
            .Which.Message.Should().Contain(Zz69).And.Contain("warping");
    }

    [Fact]
    public async Task ASystemOnlyAWarpReaches_IsFetchedOnce_EvenWithNoWaypointToCache()
    {
        // A fetch that answers is final: a system with no waypoint isn't asked for again on every pass after 5 minutes.
        await SeedAsync(scanned: [Gt9]);
        _port.GetWaypointsAsync(Zz69, 1, 20, Arg.Any<CancellationToken>()).Returns(new PagedResult<WaypointDataModel>([], 0, 1, 20));

        await PassesAsync(3);

        await _port.Received(1).GetSystemAsync(Zz69, Arg.Any<CancellationToken>());
        (await StateAsync()).Systems.Single(system => system.SystemSymbol == Zz69).WaypointsFetchedAt.Should().NotBeNull();
        (await GoalAsync(Explorer)).Should().BeNull("no waypoint to land at, so no warp goes there");
    }

    [Fact]
    public async Task ASystemOnlyAWarpReaches_IsFetchedBeforeTheGatesDueALookAgain_SoTheExplorersDontWaitBehindThem()
    {
        // B78: every explorer waits until the waypoints of a system only a warp reaches are fetched (ExploreStepKind.Wait), and that
        // fetch came after every other look, one a pass. On 2026-10-08 the plan knew 24 gates under construction, each looked at
        // again hourly, and a pass came every 2 to 9 minutes: the hourly looks never ran out, and from about 19:50Z to 04:30Z the
        // 5 explorers waited, each with an explore assignment and no goal, until X1-BS22 and X1-KA53 were fetched at 04:26:49 and
        // 04:30:07. Here 3 explored systems' gates under construction are due a look again.
        await SeedAsync();
        await AddGatesDueAgainAsync("X1-UC1", "X1-UC2", "X1-UC3");

        await PassAsync();

        await _port.Received(1).GetWaypointsAsync(Zz69, 1, 20, Arg.Any<CancellationToken>());
        (await GoalAsync(Explorer)).Should().BeOfType<WarpGoal>().Which.DestinationWaypointSymbol.Should().Be(Zz69Gate);

        await PassesAsync(3);

        await _port.Received(3).GetWaypointAsync(Arg.Any<string>(), Arg.Is<string>(gate => gate.StartsWith("X1-UC", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASystemOnlyAWarpReaches_DoesntCountTowardsTheExplorersWanted()
    {
        // D107, "No, gates only": D102 counts the systems the gates reach from home.
        await SeedAsync();

        await PassAsync();

        var state = await StateAsync();
        state.SystemsLeft.Should().Be(0);
        state.ExplorersWanted.Should().Be(0);
    }

    [Theory]
    [InlineData(1, "X1-S01")]
    [InlineData(2, Zz69)]
    public async Task TheRings_ComeInTurn_ASystemBehindAGateUnderConstructionInTheRingOfItsGate(int reach, string target)
    {
        // Slice 6.33 (D114), "Ring of their gate": X1-S01 lies 1 jump from home and 2 from the explorer at X1-GT9, after a
        // cooldown of some 930 seconds; X1-ZZ69, behind X1-GT9's gate under construction, counts 2 jumps from home through it and
        // is a warp of 753 seconds away. With rings 1 jump wide X1-S01's comes first; 2 wide, both lie in the first, and the
        // nearest by the seconds goes first. D106 took X1-S01 first at a reach of 2: the gates' systems within it first.
        await SeedAsync();
        await AddSystemAsync("X1-S01", from: Home, explored: false);
        _settings.GetAsync<int>(ExplorePlanService.RingWidthSetting, Arg.Any<CancellationToken>()).Returns(reach);

        await PassAsync();

        (await StateAsync()).Explorers.Single().TargetSystemSymbol.Should().Be(target);
        if (target == Zz69)
        {
            (await GoalAsync(Explorer)).Should().BeOfType<WarpGoal>();
        }
        else
        {
            (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = HomeGate });
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task ASystemFoundByAScan_WithNoGateKnown_ComesAfterEveryRing(int reach)
    {
        // D114, "Ring of their gate": X1-NEW, found by a scan, is a warp of some 500 seconds from the explorer at X1-GT9; no known
        // gate leads there. X1-S01, 2 jumps from home by X1-MID, is 3 from the explorer and some 930 seconds of cooldown away.
        // With rings of 1 jump, X1-S01 lies in the second, beyond D106's reach, where the nearest by the seconds went first:
        // X1-NEW. Now any ring comes before X1-NEW.
        await SeedAsync(zz69Explored: true);
        await AddSystemAsync("X1-MID", from: Home, explored: true);
        await AddSystemAsync("X1-S01", from: "X1-MID", explored: false);
        await AddScannedSystemAsync("X1-NEW", 17833, 3477);
        _settings.GetAsync<int>(ExplorePlanService.RingWidthSetting, Arg.Any<CancellationToken>()).Returns(reach);

        await PassAsync();

        (await StateAsync()).Explorers.Single().TargetSystemSymbol.Should().Be("X1-S01");
        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = HomeGate });
    }

    [Fact]
    public async Task WithNothingLeft_TheExplorerScansOnce_AndWarpsToWhatItFinds()
    {
        // D105, "Scan when none left": X1-ZZ69 explored, nothing is left within its ways. From X1-GT9 it scans: X1-NEW, 360
        // away, joins the plan; X1-AFAR, 1,500 away, beyond the 800 the tank warps, is only cached. The next pass fetches
        // X1-NEW's waypoints and warps there.
        await SeedAsync(zz69Explored: true);
        _port.ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>()).Returns(new ScanSystemsActionResult(
            [
                new ScannedSystemModel("X1-NEW", "X1", "RED_STAR", 17833, 3477, 360),
                new ScannedSystemModel("X1-AFAR", "X1", "RED_STAR", 16333, 3117, 1500),
            ],
            70,
            _now.AddSeconds(70)));
        _port.GetSystemAsync("X1-NEW", Arg.Any<CancellationToken>()).Returns(new SystemDataModel("X1-NEW", "X1", "RED_STAR", 17833, 3477));
        _port.GetWaypointsAsync("X1-NEW", 1, 20, Arg.Any<CancellationToken>()).Returns(new PagedResult<WaypointDataModel>(
            [new WaypointDataModel("X1-NEW-A1", "X1-NEW", "PLANET", 0, 0, HasMarket: true, HasShipyard: false, TraitsJson: Market)],
            1,
            1,
            20));

        await PassAsync();

        await _port.Received(1).ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>());
        var state = await StateAsync();
        state.Systems.Select(system => system.SystemSymbol).Should().Contain("X1-NEW").And.NotContain("X1-AFAR");
        state.Systems.Single(system => system.SystemSymbol == Gt9).ScannedAt.Should().NotBeNull();
        state.Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Exploring, Reason = "scanning" });
        await using (var db = TestDbContextFactory.Create(_database))
        {
            (await new SystemRepository(db).FindAsync("X1-AFAR")).Should().BeEquivalentTo(new { X = 16333, Y = 3117 });
            (await new ShipRepository(db).FindAsync(Explorer))!.CooldownExpiresAt.Should().NotBeNull();
        }

        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.SystemsScanned)
            .Which.Properties.Should().Contain(new KeyValuePair<string, object?>("Within", 1)).And.Contain(new KeyValuePair<string, object?>("Added", 1));

        await PassesAsync(2);

        await _port.Received(1).ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>());
        (await GoalAsync(Explorer)).Should().BeOfType<WarpGoal>().Which.DestinationWaypointSymbol.Should().Be("X1-NEW-A1");
    }

    [Fact]
    public async Task AScanThatFindsNothingWithinItsWarps_LeavesTheExplorerToTrade()
    {
        // The plan keeps the explorer for its scan; with nothing found within 800, the next pass releases it (D102), and it
        // doesn't scan from X1-GT9 again.
        await SeedAsync(zz69Explored: true);
        _port.ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>()).Returns(new ScanSystemsActionResult(
            [new ScannedSystemModel("X1-AFAR", "X1", "RED_STAR", 16333, 3117, 1500)],
            70,
            _now.AddSeconds(70)));

        await PassAsync();

        (await AssignmentAsync(Explorer)).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });

        await PassesAsync(2);

        await _port.Received(1).ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>());
        (await AssignmentAsync(Explorer))!.CompletedAt.Should().NotBeNull();
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Waiting, Reason = "nothing_to_explore" });
    }

    [Fact]
    public async Task AScan_WaitsForTheShipsCooldown()
    {
        // The API refuses a scan during a cooldown, as after a jump.
        await SeedAsync(zz69Explored: true);
        await using (var db = TestDbContextFactory.Create(_database))
        {
            await new ShipRepository(db).UpdateCooldownAsync(Explorer, _now.AddMinutes(4));
        }

        await PassAsync();

        await _port.DidNotReceiveWithAnyArgs().ScanSystemsAsync(default!, default);
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Exploring, Reason = "scanning" });
        (await AssignmentAsync(Explorer))!.CompletedAt.Should().BeNull("it waits for its scan");
    }

    [Fact]
    public async Task AnExplorerDockedAfterATrade_GoesIntoOrbitToScan()
    {
        // B75: the API scans only from orbit, and an explorer with nothing left to explore comes back to the plan docked, after a
        // trade (D102). Every scan from 2026-10-06 22:55Z failed so, 38 in the 24 hours to 2026-10-07 13:30Z: "Ship action
        // failed. Ship is not currently in orbit at X1-MG87-EA1C."
        await SeedAsync(zz69Explored: true);
        await MoveAsync(Explorer, Gt9, "X1-GT9-B1");
        var inOrbit = false;
        _port.OrbitShipAsync(Explorer, Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", Gt9, "X1-GT9-B1", "CRUISE", "X1-GT9-B1", _now))
            .AndDoes(_ => inOrbit = true);
        _port.ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>()).Returns(_ => inOrbit
            ? new ScanSystemsActionResult([new ScannedSystemModel("X1-AFAR", "X1", "RED_STAR", 16333, 3117, 1500)], 70, _now.AddSeconds(70))
            : throw new InvalidOperationException("Ship action failed. Ship is not currently in orbit at X1-GT9-B1."));

        await PassAsync();

        await _port.Received(1).OrbitShipAsync(Explorer, Arg.Any<CancellationToken>());
        (await StateAsync()).Systems.Single(system => system.SystemSymbol == Gt9).ScannedAt.Should().NotBeNull();
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.SystemsScanned);
    }

    [Fact]
    public async Task AnExplorerInOrbit_ScansWithoutAnotherOrbit()
    {
        await SeedAsync(zz69Explored: true);
        _port.ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>()).Returns(new ScanSystemsActionResult(
            [new ScannedSystemModel("X1-AFAR", "X1", "RED_STAR", 16333, 3117, 1500)],
            70,
            _now.AddSeconds(70)));

        await PassAsync();

        await _port.DidNotReceiveWithAnyArgs().OrbitShipAsync(default!, default);
        await _port.Received(1).ScanSystemsAsync(Explorer, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNothingLeft_WhereTheGatesDontReach_TheExplorerWarpsBack_AndIsReleasedThereToTrade()
    {
        // Done when: "the explorer warps to such systems and back without being stranded". In X1-ZZ69, explored and scanned from,
        // nothing is left: it warps back to the nearest system the gates reach, X1-GT9, and is released there (D102).
        await SeedAsync(zz69Explored: true, scanned: [Gt9, Zz69]);
        await FetchZz69Async();
        await MoveAsync(Explorer, Zz69, "X1-ZZ69-A1");

        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeOfType<WarpGoal>().Which.DestinationWaypointSymbol.Should().Be(Gt9Gate);
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Returning, TargetSystemSymbol = Gt9, Reason = "nothing_to_explore" });
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.PlanStarted).Which.Message.Should().Contain("goes back to");

        await MoveAsync(Explorer, Gt9, Gt9Gate);
        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeNull();
        (await AssignmentAsync(Explorer))!.CompletedAt.Should().NotBeNull("it trades from there until a system turns up");
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Waiting, Reason = "nothing_to_explore" });
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(61, true)]
    public async Task AWarpTheApiRefused_IsDropped_AndThatSystemGetsNoWarpForAnHour(int minutesAgo, bool warpsAgain)
    {
        await SeedAsync(scanned: [Gt9]);
        await FetchZz69Async();
        await TakeAsync(Explorer);
        await SetGoalAsync(Explorer, new WarpGoal { DestinationWaypointSymbol = Zz69Gate, Status = GoalStatus.Blocked, StatusReason = WarpGoalExecutor.RefusedReason });
        _warpRefusals.Record(Zz69, _now.AddMinutes(-minutesAgo));

        await PassAsync();

        if (warpsAgain)
        {
            (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { DestinationWaypointSymbol = Zz69Gate, Status = GoalStatus.Assigned });
        }
        else
        {
            (await GoalAsync(Explorer)).Should().BeNull();
            (await AssignmentAsync(Explorer))!.CompletedAt.Should().NotBeNull("nothing is left for it but trading");
        }
    }

    /// <summary>
    /// The agent with 2,000,000 credits, its command ship home with nothing to do (an explorer explores), the explorer in orbit
    /// at X1-GT9's gate, and the plan's map: home and X1-GT9 explored, their positions and waypoints cached; X1-ZZ69 behind a
    /// gate under construction, its position and waypoints not fetched yet.
    /// </summary>
    private async Task SeedAsync(bool zz69Explored = false, IReadOnlyList<string>? scanned = null)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new AgentRepository(db).UpsertAsync(new AgentModel("SPECTER", "account", "X1-FJ91-A1", 2_000_000, "COSMIC", 21));
        await new ShipRepository(db).UpsertAsync(new ShipModel(CommandShip, Home, "X1-FJ91-A1", "DOCKED", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: ExplorePlanService.CommandShipType));
        await new ShipRepository(db).UpsertAsync(new ShipModel(
            Explorer,
            Gt9,
            Gt9Gate,
            "IN_ORBIT",
            "CRUISE",
            800,
            800,
            CargoCapacity: 40,
            ShipType: "SHIP_EXPLORER",
            MountSymbols: ["MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II"],
            ModulesJson: WarpsTests.ExplorerModules,
            EngineJson: """{"symbol":"ENGINE_ION_DRIVE_II","speed":36}"""));
        var systems = new SystemRepository(db);
        await systems.UpsertAsync(new SystemCacheModel(Home, "X1", "BLUE_STAR", 20763, 2994, _now));
        await systems.UpsertAsync(new SystemCacheModel(Gt9, "X1", "ORANGE_STAR", 17833, 3117, _now));
        await new WaypointRepository(db).UpsertRangeAsync(
        [
            new WaypointCacheModel("X1-FJ91-A1", Home, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, Market),
            new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 100, 0, HasMarket: true, HasShipyard: false, _now, Market),
            new WaypointCacheModel(Gt9Gate, Gt9, "JUMP_GATE", 0, 0, HasMarket: true, HasShipyard: false, _now, Market),
            new WaypointCacheModel("X1-GT9-B1", Gt9, "PLANET", -50, 0, HasMarket: true, HasShipyard: false, _now, Market),
        ]);
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, new ExplorePlanState
        {
            ShipSymbol = CommandShip,
            HomeSystemSymbol = Home,
            Status = ExploreStatus.Done,
            UpdatedAt = _now,
            Systems =
            [
                Known(Home, HomeGate, explored: true, connections: [Gt9Gate]) with { ScannedAt = Scanned(Home, scanned) },
                Known(Gt9, Gt9Gate, explored: true, connections: [HomeGate, Zz69Gate]) with { ScannedAt = Scanned(Gt9, scanned) },
                Known(Zz69, Zz69Gate, explored: zz69Explored) with { Gate = GateState.UnderConstruction, ScannedAt = Scanned(Zz69, scanned) },
            ],
        });
    }

    private DateTimeOffset? Scanned(string system, IReadOnlyList<string>? scanned) => scanned?.Contains(system) == true ? _now.AddHours(-1) : null;

    /// <summary>X1-ZZ69's position and waypoints in the cache, as the plan fetches them.</summary>
    private async Task FetchZz69Async()
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new SystemRepository(db).UpsertAsync(new SystemCacheModel(Zz69, "X1", "ORANGE_STAR", 17776, 3645, _now));
        await new WaypointRepository(db).UpsertRangeAsync(
        [
            new WaypointCacheModel(Zz69Gate, Zz69, "JUMP_GATE", 10, -20, HasMarket: true, HasShipyard: false, _now, Market) { IsUnderConstruction = true },
            new WaypointCacheModel("X1-ZZ69-A1", Zz69, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, Market),
        ]);
    }

    /// <summary>A system the plan has looked at: its gate built, and, once explored, its connections known.</summary>
    private KnownSystem Known(string system, string gate, bool explored, IReadOnlyList<string>? connections = null) => new()
    {
        SystemSymbol = system,
        GateWaypointSymbol = gate,
        Gate = GateState.Active,
        GateCheckedAt = _now,
        Connections = explored ? connections ?? [] : null,
        ConnectionsCheckedAt = explored ? _now : null,
        WaypointsCheckedAt = explored ? _now : null,
        ExploredAt = explored ? _now.AddHours(-1) : null,
    };

    /// <summary>A new system behind <paramref name="from"/>'s gate, as the plan learns of it from that gate's connections.</summary>
    private async Task AddSystemAsync(string system, string from, bool explored)
    {
        var state = await StateAsync();
        state = state with
        {
            Systems =
            [
                .. state.Systems.Select(known => known.SystemSymbol == from ? known with { Connections = [.. known.Connections ?? [], $"{system}-G"] } : known),
                Known(system, $"{system}-G", explored),
            ],
        };
        await using var db = TestDbContextFactory.Create(_database);
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, state);
    }

    /// <summary>
    /// Explored systems whose gates are still under construction, last looked at 2 hours ago, so each is due a look again
    /// (<see cref="ExploreAtlas.RecheckAfter"/>); the API says they are still under construction.
    /// </summary>
    private async Task AddGatesDueAgainAsync(params string[] systems)
    {
        var state = await StateAsync();
        state = state with
        {
            Systems =
            [
                .. state.Systems,
                .. systems.Select(system => Known(system, $"{system}-G", explored: true) with
                {
                    Gate = GateState.UnderConstruction,
                    GateCheckedAt = _now.AddHours(-2),
                    WaypointsFetchedAt = _now.AddHours(-2),
                }),
            ],
        };
        await using var db = TestDbContextFactory.Create(_database);
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, state);
        foreach (var system in systems)
        {
            _port.GetWaypointAsync(system, $"{system}-G", Arg.Any<CancellationToken>())
                .Returns(new WaypointDataModel($"{system}-G", system, "JUMP_GATE", 0, 0, HasMarket: false, HasShipyard: false, TraitsJson: "[]", IsUnderConstruction: true));
        }
    }

    /// <summary>
    /// A system a scan found (D105), as the plan keeps it: known, with no gate, its position and a market's waypoint cached, its
    /// waypoints fetched.
    /// </summary>
    private async Task AddScannedSystemAsync(string system, int x, int y)
    {
        var state = await StateAsync();
        state = state with { Systems = [.. state.Systems, new KnownSystem { SystemSymbol = system, WaypointsFetchedAt = _now }] };
        await using var db = TestDbContextFactory.Create(_database);
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, state);
        await new SystemRepository(db).UpsertAsync(new SystemCacheModel(system, "X1", "RED_STAR", x, y, _now));
        await new WaypointRepository(db).UpsertRangeAsync(
            [new WaypointCacheModel($"{system}-A1", system, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, Market)]);
    }

    /// <summary>The ship is at <paramref name="waypoint"/>, docked, its goal ended: a flight or a warp's arrival.</summary>
    private async Task MoveAsync(string ship, string system, string waypoint)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipRepository(db).UpdateNavAsync(ship, new NavModel("DOCKED", system, waypoint, "CRUISE", waypoint, _now.AddSeconds(-1)), null);
        await new ShipGoalRepository(db).ClearActiveGoalAsync(ship);
    }

    private async Task TakeAsync(string ship)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipAssignmentRepository(db).UpsertAsync(new ShipAssignmentDto(ship, ExplorePlanService.AssignmentType, null, null, null, null, 0, _now, null));
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
                new OrbitSubCommand(_port, new ShipRepository(db), new MarketRepository(db), Substitute.For<IRefuelSubCommand>(), _log.For<OrbitSubCommand>()),
                _purchases,
                _order,
                new JumpRefusals(),
                _warpRefusals,
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

    private async Task<ShipGoal?> GoalAsync(string ship)
    {
        await using var db = TestDbContextFactory.Create(_database);
        return await new ShipGoalRepository(db).GetActiveGoalAsync(ship);
    }

    private async Task SetGoalAsync(string ship, ShipGoal goal)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipGoalRepository(db).SetActiveGoalAsync(ship, goal);
    }

    private async Task<ShipAssignmentDto?> AssignmentAsync(string ship)
    {
        await using var db = TestDbContextFactory.Create(_database);
        return await new ShipAssignmentRepository(db).FindAsync(ship);
    }

    private async Task<ExplorePlanState> StateAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        return (await new PlanRepository(db).GetAsync<ExplorePlanState>(PlanTypes.Explore))!;
    }
}

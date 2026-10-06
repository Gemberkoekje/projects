using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Exploring;

/// <summary>
/// Slice 6.31: an explorer's warp (D100, D104), as every warp goes (<see cref="GoalWarps"/>). From X1-GT9's gate, a market, to
/// X1-ZZ69, 531 away, whose gate is a market under construction: a full tank of 800 doesn't pay for BURN's 1,062, so it
/// cruises, 531 fuel and 753 seconds at speed 36 by the research note. The cache is the real repositories'; the API's warp and
/// the ship's other commands are fakes.
/// </summary>
public sealed class WarpGoalExecutorTests
{
    private const string Ship = "SPECTER-50";
    private const string Gt9 = "X1-GT9";
    private const string Gt9Gate = "X1-GT9-E10Z";
    private const string Zz69 = "X1-ZZ69";
    private const string Zz69Gate = "X1-ZZ69-I53";
    private const string Market = """[{"symbol":"MARKETPLACE"}]""";

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly IWarpSubCommand _warp = Substitute.For<IWarpSubCommand>();
    private readonly IFlightModeSubCommand _flightMode = Substitute.For<IFlightModeSubCommand>();
    private readonly IDockSubCommand _dock = Substitute.For<IDockSubCommand>();
    private readonly IOrbitSubCommand _orbit = Substitute.For<IOrbitSubCommand>();
    private readonly IRefuelSubCommand _refuel = Substitute.For<IRefuelSubCommand>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly WarpRefusals _refusals = new();
    private readonly LogRecorder _log = new();
    private readonly WarpGoal _goal = new() { DestinationWaypointSymbol = Zz69Gate };

    public WarpGoalExecutorTests()
    {
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(new ExplorePlanState
        {
            ShipSymbol = "SPECTER-1",
            HomeSystemSymbol = "X1-FJ91",
            Status = ExploreStatus.Done,
            UpdatedAt = _now,
            Systems =
            [
                new KnownSystem { SystemSymbol = Gt9, GateWaypointSymbol = Gt9Gate, Gate = GateState.Active, ExploredAt = _now },
                new KnownSystem { SystemSymbol = Zz69, GateWaypointSymbol = Zz69Gate, Gate = GateState.UnderConstruction },
            ],
        });
        WarpLands(seconds: 753, fuelLeft: 269);
    }

    [Fact]
    public async Task AWarp_GoesInTheModeTheFuelPaysFor_AndIsJournalledWithWhatItTook_AgainstTheNote()
    {
        await SeedAsync(At(Gt9Gate, "IN_ORBIT"));

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        Received.InOrder(() =>
        {
            _flightMode.EnsureAsync(Arg.Is<ShipModel>(ship => ship.Symbol == Ship), "CRUISE", Arg.Any<CancellationToken>());
            _warp.ExecuteAsync(Ship, Zz69Gate, _goal.GoalId, Arg.Any<CancellationToken>());
        });
        await _dock.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        var warped = _log.Journal.Should().ContainSingle().Subject;
        warped.EventKind.Should().Be(JournalEvents.Warped);
        warped.Properties.Should().Contain(new KeyValuePair<string, object?>("Fuel", 531))
            .And.Contain(new KeyValuePair<string, object?>("ReckonedFuel", 531))
            .And.Contain(new KeyValuePair<string, object?>("Seconds", 753.0))
            .And.Contain(new KeyValuePair<string, object?>("ReckonedSeconds", 753.0));
        _log.Entries.Should().NotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AWarpThatDiffersFromTheNote_SaysSo()
    {
        // D100, "then measure": the first warp checks the note; one that differs logs a warning, for the note to take the API's
        // numbers.
        await SeedAsync(At(Gt9Gate, "IN_ORBIT"));
        WarpLands(seconds: 900, fuelLeft: 269);

        await StepAsync();

        _log.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Which.Message.Should().Contain("900").And.Contain("753");
    }

    [Fact]
    public async Task InOrbitAtAMarket_ShortOfTheFuel_ItDocksToRefuelFirst()
    {
        // 500 aboard doesn't pay for the 531; a full tank does. Only a docked ship refuels, as it orbits again.
        await SeedAsync(At(Gt9Gate, "IN_ORBIT") with { FuelCurrent = 500 });

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        await _dock.Received(1).ExecuteAsync(Ship, Arg.Any<CancellationToken>());
        await _warp.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);
    }

    [Fact]
    public async Task DockedAtAMarket_ItFillsTheTank_Orbits_AndWarps()
    {
        // The cache has the tank the refuel fills; the step starts from the ship before it.
        await SeedAsync(At(Gt9Gate, "DOCKED"));

        await StepAsync(arriving: At(Gt9Gate, "DOCKED") with { FuelCurrent = 500 });

        Received.InOrder(() =>
        {
            _refuel.ExecuteAsync(Ship, false, Arg.Any<CancellationToken>());
            _orbit.ExecuteAsync(Ship, Arg.Any<CancellationToken>());
            _warp.ExecuteAsync(Ship, Zz69Gate, _goal.GoalId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ATankTheRefuelDidntFill_AsksTheApiForNoWarp_AndThePlanChoosesAgain()
    {
        await SeedAsync(At(Gt9Gate, "DOCKED") with { FuelCurrent = 500 });

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
        (await GoalAsync()).Should().BeNull();
        await _warp.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);
    }

    [Fact]
    public async Task ShortOfTheFuelWhereItCantRefuel_ItFliesToItsSystemsNearestMarketFirst()
    {
        await SeedAsync(At("X1-GT9-ROCK", "IN_ORBIT") with { FuelCurrent = 100 });

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        await _bus.Received(1).InvokeAsync(Arg.Is<NavigateToWaypointCommand>(command => command.DestinationWaypoint == "X1-GT9-B1"), Arg.Any<CancellationToken>());
        await _warp.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);
    }

    [Fact]
    public async Task ABurnWarpTheApiRefusesForItsFuel_GoesInCruise()
    {
        // X1-NEAR lies 300 from X1-GT9: 600 fuel pays for BURN. BURN's fuel isn't confirmed for warps since API 2.1 (D104);
        // the API's answer decides.
        await SeedAsync(At(Gt9Gate, "IN_ORBIT"), near: true);
        _warp.ExecuteAsync(Ship, "X1-NEAR-A1", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw new WarpRefusedException("X1-NEAR-A1", WarpRefusedException.InsufficientFuel, "not enough fuel", new InvalidOperationException("400")),
            _ => Landing("X1-NEAR-A1", 432, 500));

        var result = await StepAsync(new WarpGoal { DestinationWaypointSymbol = "X1-NEAR-A1" });

        result.Outcome.Should().Be(GoalExecutionOutcome.WaitingForArrival);
        Received.InOrder(() =>
        {
            _flightMode.EnsureAsync(Arg.Any<ShipModel>(), "BURN", Arg.Any<CancellationToken>());
            _flightMode.EnsureAsync(Arg.Any<ShipModel>(), "CRUISE", Arg.Any<CancellationToken>());
        });
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.Warped).Which.Properties["FlightMode"].Should().Be("CRUISE");
        _refusals.Refused(DateTimeOffset.UtcNow).Should().BeEmpty();
    }

    [Fact]
    public async Task AWarpTheApiRefuses_BlocksTheGoal_AndNoWarpGoesToThatSystemForAnHour()
    {
        await SeedAsync(At(Gt9Gate, "IN_ORBIT"));
        _warp.ExecuteAsync(Ship, Zz69Gate, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new WarpRefusedException(Zz69Gate, 4241, "no warp drive", new InvalidOperationException("400")));

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.Blocked);
        (await GoalAsync()).Should().BeEquivalentTo(new { Status = GoalStatus.Blocked, StatusReason = WarpGoalExecutor.RefusedReason });
        _refusals.Refused(DateTimeOffset.UtcNow).Should().BeEquivalentTo([Zz69]);
        _refusals.Refused(DateTimeOffset.UtcNow.AddMinutes(61)).Should().BeEmpty();
        _log.Journal.Should().ContainSingle().Which.EventKind.Should().Be(JournalEvents.ShipBlocked);
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(450, false)]
    public async Task IntoASystemWithNowhereToRefuel_ItWarpsOnlyWithTheFuelToWarpBack(int distance, bool warps)
    {
        // D100: "a warp only where the ship can refuel, or has the fuel to warp back".
        await SeedAsync(At(Gt9Gate, "IN_ORBIT"), bare: distance);
        _warp.ExecuteAsync(Ship, "X1-BARE-ROCK", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Landing("X1-BARE-ROCK", 432, 500));

        var result = await StepAsync(new WarpGoal { DestinationWaypointSymbol = "X1-BARE-ROCK" });

        if (warps)
        {
            await _warp.Received(1).ExecuteAsync(Ship, "X1-BARE-ROCK", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            await _flightMode.Received(1).EnsureAsync(Arg.Any<ShipModel>(), "CRUISE", Arg.Any<CancellationToken>());
        }
        else
        {
            result.Outcome.Should().Be(GoalExecutionOutcome.Progressing);
            (await GoalAsync()).Should().BeNull("the explore plan chooses again");
            await _warp.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);
        }
    }

    [Fact]
    public async Task InTheDestinationsSystem_TheGoalEnds()
    {
        await SeedAsync(new ShipModel(Ship, Zz69, Zz69Gate, "DOCKED", "CRUISE", 269, 800, ShipType: "SHIP_EXPLORER", ModulesJson: WarpsTests.ExplorerModules));

        var result = await StepAsync();

        result.Outcome.Should().Be(GoalExecutionOutcome.Completed);
        (await GoalAsync()).Should().BeNull();
        await _warp.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, Guid.Empty, default);
    }

    private static ShipModel At(string waypoint, string status)
        => new(
            Ship,
            Gt9,
            waypoint,
            status,
            "CRUISE",
            800,
            800,
            CargoCapacity: 40,
            ShipType: "SHIP_EXPLORER",
            ModulesJson: WarpsTests.ExplorerModules,
            EngineJson: """{"symbol":"ENGINE_ION_DRIVE_II","speed":36}""");

    private static WarpActionResult Landing(string destination, double seconds, int fuelLeft)
        => new(
            new NavModel("IN_TRANSIT", WaypointSymbols.SystemOf(destination), destination, "CRUISE", destination, DateTimeOffset.UtcNow.AddSeconds(seconds)),
            new FuelModel(fuelLeft, 800));

    /// <summary>The API's warp to X1-ZZ69's gate lands <paramref name="seconds"/> after the call, with <paramref name="fuelLeft"/> left.</summary>
    private void WarpLands(double seconds, int fuelLeft)
        => _warp.ExecuteAsync(Ship, Zz69Gate, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => Landing(Zz69Gate, seconds, fuelLeft));

    /// <summary>
    /// The ship and its goal; X1-GT9 and X1-ZZ69 with their positions and waypoints cached; with <paramref name="near"/>, X1-NEAR
    /// 300 from X1-GT9, a market; with <paramref name="bare"/>, X1-BARE that far from X1-GT9, with nowhere to refuel.
    /// </summary>
    private async Task SeedAsync(ShipModel ship, bool near = false, int bare = 0)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipRepository(db).UpsertAsync(ship);
        await new ShipGoalRepository(db).SetActiveGoalAsync(Ship, _goal);
        var systems = new SystemRepository(db);
        await systems.UpsertAsync(new SystemCacheModel(Gt9, "X1", "ORANGE_STAR", 17833, 3117, _now));
        await systems.UpsertAsync(new SystemCacheModel(Zz69, "X1", "ORANGE_STAR", 17776, 3645, _now));
        List<WaypointCacheModel> waypoints =
        [
            new(Gt9Gate, Gt9, "JUMP_GATE", 0, 0, HasMarket: true, HasShipyard: false, _now, Market),
            new("X1-GT9-B1", Gt9, "PLANET", 250, 0, HasMarket: true, HasShipyard: false, _now, Market),
            new("X1-GT9-ROCK", Gt9, "ASTEROID", 300, 0, HasMarket: false, HasShipyard: false, _now, "[]"),
            new(Zz69Gate, Zz69, "JUMP_GATE", 10, -20, HasMarket: true, HasShipyard: false, _now, Market),
            new("X1-ZZ69-A1", Zz69, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, Market),
        ];
        if (near)
        {
            await systems.UpsertAsync(new SystemCacheModel("X1-NEAR", "X1", "RED_STAR", 17833, 3417, _now));
            waypoints.Add(new WaypointCacheModel("X1-NEAR-A1", "X1-NEAR", "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, Market));
        }

        if (bare > 0)
        {
            await systems.UpsertAsync(new SystemCacheModel("X1-BARE", "X1", "RED_STAR", 17833, 3117 + bare, _now));
            waypoints.Add(new WaypointCacheModel("X1-BARE-ROCK", "X1-BARE", "ASTEROID", 0, 0, HasMarket: false, HasShipyard: false, _now, "[]"));
        }

        await new WaypointRepository(db).UpsertRangeAsync(waypoints);
        _tradeContexts.ReadAsync(Gt9, Arg.Any<CancellationToken>()).Returns(new TradeContext(
            new TradeMarketMap(waypoints.Where(waypoint => waypoint.SystemSymbol == Gt9), [], new Dictionary<string, IReadOnlyList<string>>()),
            0,
            0));
    }

    /// <summary>One step of the goal, for the ship as the cache has it, or as <paramref name="arriving"/> says it was before.</summary>
    private async Task<GoalExecutionResult> StepAsync(WarpGoal? goal = null, ShipModel? arriving = null)
    {
        await using var db = TestDbContextFactory.Create(_database);
        var goals = new ShipGoalRepository(db);
        if (goal is not null)
        {
            await goals.SetActiveGoalAsync(Ship, goal);
        }

        var ships = new ShipRepository(db);
        var ship = arriving ?? (await ships.FindAsync(Ship))!;

        return await new WarpGoalExecutor(
                goals,
                new GoalWarps(
                    ships,
                    goals,
                    new SystemRepository(db),
                    new WaypointRepository(db),
                    _gates,
                    _refusals,
                    _tradeContexts,
                    _dock,
                    _orbit,
                    _refuel,
                    _flightMode,
                    _warp,
                    _bus,
                    _log.For<GoalWarps>()),
                _log.For<WarpGoalExecutor>())
            .ExecuteStepAsync(ship, goal ?? _goal, new ShipGoalContext(), CancellationToken.None);
    }

    private async Task<ShipGoal?> GoalAsync()
    {
        await using var db = TestDbContextFactory.Create(_database);
        return await new ShipGoalRepository(db).GetActiveGoalAsync(Ship);
    }
}

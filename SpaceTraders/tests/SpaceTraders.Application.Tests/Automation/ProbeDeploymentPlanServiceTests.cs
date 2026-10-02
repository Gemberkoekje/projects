using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.3 (D29, D30): a probe for every market of the headquarters' system, bought while the credits stay
/// at the reserve; until then the probes roam between nearby markets, the one whose prices are oldest first,
/// and a shipyard where a purchase waits for one of our ships gets the nearest probe. On X1-DC53 as it was
/// on 2026-10-02, with the starting probe SPECTER-2 parked at H52 since the start.
/// </summary>
public sealed class ProbeDeploymentPlanServiceTests
{
    private const string SystemSymbol = "X1-DC53";
    private const string A1 = "X1-DC53-A1";
    private const string A2 = "X1-DC53-A2";
    private const string H51 = "X1-DC53-H51";
    private const string H52 = "X1-DC53-H52";
    private const string XB5C = "X1-DC53-XB5C";
    private const string J58 = "X1-DC53-J58";

    private readonly IProbeDeploymentPlanRepository _plans = Substitute.For<IProbeDeploymentPlanRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly ShipyardCalls _calls = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _now = TimeProvider.System.GetUtcNow();
    private ProbeDeploymentPlanState? _state;

    public ProbeDeploymentPlanServiceTests()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, A1, 151_214, "COBALT", 3));
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
        [
            Waypoint(A1, "PLANET", 21, 16),
            Waypoint(A2, "MOON", 21, 16, shipyard: true),
            Waypoint(H51, "PLANET", -18, 40),
            Waypoint(H52, "MOON", -18, 40, shipyard: true),
            Waypoint(XB5C, "ENGINEERED_ASTEROID", -15, 21),
            Waypoint(J58, "ASTEROID_BASE", 435, -572),
        ]);
        LastSeen((A1, 60), (A2, 2), (H51, 30), (H52, 1), (XB5C, 1), (J58, 90));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645), ("SHIP_LIGHT_SHUTTLE", 117_273)),
            Shipyard("X1-DC53-C39", ("SHIP_PROBE", 103_729)),
            Shipyard(H52, ("SHIP_MINING_DRONE", 48_328)),
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { Failure = ShipPurchaseFailure.OverBudget, FailureReason = "over budget", EstimatedCost = 81_645 });
        _settings.GetAsync<int>(MarketWatchService.RefreshMinutesSetting, Arg.Any<CancellationToken>()).Returns(5);
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync(Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(Arg.Any<ProbeDeploymentPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.Arg<ProbeDeploymentPlanState>());
        Fleet(CommandShip(), StartingProbe(), Drone());
    }

    [Fact]
    public async Task TheStartingProbe_IsAProbe_AndFliesToTheMarketWhosePricesNeedItMost()
    {
        // B25: the old plan didn't see the starting probe, whose cached type is its role, SATELLITE. A1's
        // prices are an hour old and 143 s away; H51's half an hour, next door; J58's 90 minutes, 35 away.
        await RunAsync();

        var flight = _activeGoals["SPECTER-2"].Should().BeOfType<DeployProbeGoal>().Subject;
        flight.TargetWaypointSymbol.Should().Be(A1);
        flight.ForPurchase.Should().BeFalse();
        _activeGoals.Should().NotContainKeys("SPECTER-1", "SPECTER-3");
    }

    [Fact]
    public async Task WithFewerProbesThanMarkets_ItBuysAProbe_WhereProbesCostLeast()
    {
        // D29: SHIP_PROBE, while the credits stay at the reserve, which the purchase checks.
        await RunAsync();

        await _purchases.Received(1).TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>());
        _state!.NextProbeShipyard.Should().Be(A2);
        _state.NextProbePrice.Should().Be(81_645);
    }

    [Fact]
    public async Task WithAProbeAtEveryMarket_ItBuysNone_AndTheProbesStay()
    {
        // D29's long-term goal. Each probe watches its own market; the market watch keeps them fresh.
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
            [Waypoint(A2, "MOON", 21, 16, shipyard: true), Waypoint(H52, "MOON", -18, 40, shipyard: true)]);
        LastSeen((A2, 3), (H52, 3));
        Fleet(StartingProbe(), Probe("SPECTER-5", A2));

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _activeGoals.Should().BeEmpty();
        _state!.Purchase.Should().Be(ProbePurchaseStatus.EveryMarketHasOne);
        _state.Markets.Select(market => market.ProbeSymbol).Should().Equal("SPECTER-5", "SPECTER-2");
    }

    [Fact]
    public async Task WhileAProbeIsTooDear_ThePlanWaits_AndSaysSoOnce()
    {
        await RunAsync();
        await RunAsync();

        _state!.Purchase.Should().Be(ProbePurchaseStatus.WaitingForCredits);
        _log.Journal.Where(entry => entry.EventKind == "PlanBlocked").Should().ContainSingle()
            .Which.Message.Should().Be("PlanBlocked: ProbeDeployment plan waits (waiting_for_credits): a probe costs 81645 at X1-DC53-A2, and the purchase must leave the credit reserve.");
    }

    [Fact]
    public async Task AShipyardThatCalls_GetsTheNearestFreeProbe()
    {
        // D30: the purchase finds none of our ships at A2 and calls for one.
        _purchases.TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Call(A2, "SHIP_PROBE", TimeProvider.System.GetUtcNow());
            return new ShipPurchaseResult { Failure = ShipPurchaseFailure.NoShipAtShipyard, EstimatedCost = 81_645 };
        });

        await RunAsync();

        var flight = _activeGoals["SPECTER-2"].Should().BeOfType<DeployProbeGoal>().Subject;
        flight.TargetWaypointSymbol.Should().Be(A2);
        flight.ForPurchase.Should().BeTrue();
        _state!.Purchase.Should().Be(ProbePurchaseStatus.WaitingForAShipAtTheShipyard);
        _state.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo(new ProbeCallState { WaypointSymbol = A2, ShipType = "SHIP_PROBE", ProbeSymbol = "SPECTER-2" });
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "ProbeCalled")
            .Which.Message.Should().Be("ProbeCalled: probe SPECTER-2 flies to X1-DC53-A2, where a SHIP_PROBE purchase waits for one of our ships.");
    }

    [Fact]
    public async Task AProbeInFlight_KeepsItsMarket_AndCountsAsAProbe()
    {
        // B15: the old plan didn't count a probe in flight, and bought another for its target every tick.
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
            [Waypoint(A1, "PLANET", 21, 16), Waypoint(H51, "PLANET", -18, 40), Waypoint(H52, "MOON", -18, 40) with { HasMarket = false }]);
        LastSeen((A1, 60), (H51, 30));
        var flying = Probe("SPECTER-5", H52) with
        {
            Status = "IN_TRANSIT",
            DestWaypointSymbol = A1,
            ArrivesAt = _now.AddMinutes(2),
        };
        _activeGoals["SPECTER-5"] = new DeployProbeGoal { TargetWaypointSymbol = A1 };
        Fleet(StartingProbe(), flying);

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _activeGoals["SPECTER-2"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(H51);
    }

    [Fact]
    public async Task MarketsSeenWithinTheMarketWatchsInterval_AreNotDue()
    {
        _settings.GetAsync<int>(MarketWatchService.RefreshMinutesSetting, Arg.Any<CancellationToken>()).Returns(120);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task APurchaseThatFails_LeavesTheProbesFlying()
    {
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("422"));

        await RunAsync();

        _activeGoals.Should().ContainKey("SPECTER-2");
        _log.Entries.Should().Contain(entry => entry.Message.StartsWith("Probe plan: buying a SHIP_PROBE at X1-DC53-A2 failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheState_ShowsWhoWatchesEachMarket_AndIsWrittenOnlyWhenItChanges()
    {
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(Arg.Any<ProbeDeploymentPlanState>(), Arg.Any<CancellationToken>());
        _state!.Probes.Should().Be(1);
        _state.Markets.Select(market => (market.WaypointSymbol, market.ProbeSymbol, market.WatchedByShip)).Should().Equal(
            (A1, "SPECTER-2", false),
            (A2, string.Empty, false),
            (H51, string.Empty, false),
            (H52, string.Empty, false),
            (J58, string.Empty, false),
            (XB5C, string.Empty, true));
        _state.Markets.Single(market => market.WaypointSymbol == H51).DueAt.Should().BeCloseTo(_now.AddMinutes(-25), TimeSpan.FromSeconds(1));
        _state.Markets.Single(market => market.WaypointSymbol == A1).DueAt.Should().Be(default, "a watched market's time changes with every refresh");
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "PlanStarted")
            .Which.Message.Should().Be("PlanStarted: ProbeDeployment plan for system X1-DC53: 1 probes for 6 markets.");
    }

    private static WaypointCacheModel Waypoint(string symbol, string type, int x, int y, bool shipyard = false)
        => new(symbol, SystemSymbol, type, x, y, HasMarket: true, HasShipyard: shipyard, DateTimeOffset.UnixEpoch);

    private static ShipyardWaypointDto Shipyard(string waypointSymbol, params (string Type, long Price)[] ships) => new()
    {
        WaypointSymbol = waypointSymbol,
        SystemSymbol = SystemSymbol,
        ShipTypes = [.. ships.Select(ship => ship.Type)],
        Ships = [.. ships.Select(ship => new ShipyardShipDto { Type = ship.Type, PurchasePrice = ship.Price })],
    };

    private static ShipModel CommandShip()
        => new("SPECTER-1", SystemSymbol, XB5C, "IN_ORBIT", "CRUISE", 400, 400, CargoCapacity: 40, ShipType: "COMMAND", MountSymbols: ["MOUNT_SURVEYOR_II", "MOUNT_MINING_LASER_II"]);

    private static ShipModel Drone()
        => new("SPECTER-3", SystemSymbol, XB5C, "IN_ORBIT", "CRUISE", 80, 80, CargoCapacity: 15, ShipType: "EXCAVATOR", MountSymbols: ["MOUNT_MINING_LASER_I"]);

    /// <summary>The starting probe as startup sync stores it: its role as its type (B25), its frame and engine.</summary>
    private static ShipModel StartingProbe()
        => new("SPECTER-2", SystemSymbol, H52, "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE",
            FrameJson: """{"symbol":"FRAME_PROBE","fuelCapacity":0}""",
            EngineJson: """{"symbol":"ENGINE_IMPULSE_DRIVE_I","speed":9}""");

    /// <summary>A probe as a purchase stores it, before the next restart fills in its frame and engine.</summary>
    private static ShipModel Probe(string symbol, string waypointSymbol)
        => new(symbol, SystemSymbol, waypointSymbol, "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE");

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void LastSeen(params (string WaypointSymbol, int MinutesAgo)[] markets)
        => _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>())
            .Returns([.. markets.Select(market => new MarketFreshnessRecord(market.WaypointSymbol, SystemSymbol, _now.AddMinutes(-market.MinutesAgo)))]);

    private Task RunAsync()
        => new ProbeDeploymentPlanService(
                _plans,
                _agents,
                _waypoints,
                _markets,
                _ships,
                _goals,
                _shipyards,
                _purchases,
                _calls,
                _settings,
                _log.For<ProbeDeploymentPlanService>())
            .EnsureBootstrappedAsync();
}

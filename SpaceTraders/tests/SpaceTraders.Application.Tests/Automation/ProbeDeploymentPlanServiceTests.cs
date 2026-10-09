using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.3 (D29, D30): a probe for every market of the headquarters' system, bought while the credits stay
/// at the reserve; until then the probes roam between nearby markets, the one whose prices are oldest first,
/// and a shipyard where a purchase waits for one of our ships gets the nearest probe. On X1-DC53 as it was
/// on 2026-10-02, with the starting probe SPECTER-2 parked at H52 since the start. Its waypoints A2 and H52 are cached here
/// without their shipyard trait, so the probes roam as they did before slice 6.32, which parks a probe at each shipyard
/// (D110); the tests of slice 6.32 give them their trait.
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
    private const string I55 = "X1-DC53-I55";
    private const string Kr90 = "X1-KR90";
    private const string Kr90Gate = "X1-KR90-G";
    private const string Kr90K1 = "X1-KR90-K1";
    private const string Kr90K2 = "X1-KR90-K2";
    private const string Mt49 = "X1-MT49";
    private const string Mt49Gate = "X1-MT49-G";
    private const string Mt49M1 = "X1-MT49-M1";

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
    private readonly OpenPurchaseOrder _order = new();
    private readonly IGateNetwork _gates = Substitute.For<IGateNetwork>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTimeOffset _now = TimeProvider.System.GetUtcNow();
    private ProbeDeploymentPlanState? _state;

    public ProbeDeploymentPlanServiceTests()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, A1, 151_214, "COBALT", 3));
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(DefaultWaypoints());
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
    public async Task AMarketWithoutPrices_CountsAsNeverSeen_SoAProbeGoesThereFirst()
    {
        // B62, seen on the cluster on 2026-10-04: X1-FJ91-C46 lost its prices at 19:28:27Z, and from then on its last fetch
        // made it look a minute old, so no probe would go there for them. A market without prices counts as never seen.
        _markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketFreshnessRecord(A1, SystemSymbol, _now.AddMinutes(-60)),
            new MarketFreshnessRecord(A2, SystemSymbol, _now.AddMinutes(-2)),
            new MarketFreshnessRecord(H51, SystemSymbol, _now.AddMinutes(-1), HasPrices: false),
            new MarketFreshnessRecord(H52, SystemSymbol, _now.AddMinutes(-1)),
            new MarketFreshnessRecord(XB5C, SystemSymbol, _now.AddMinutes(-1)),
            new MarketFreshnessRecord(J58, SystemSymbol, _now.AddMinutes(-90)),
        ]);

        await RunAsync();

        _activeGoals["SPECTER-2"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(H51);
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
    public async Task WhereAShipyardSellsInterceptors_OneIsBoughtInsteadOfAProbe_ThoughItCostsMore()
    {
        // Slice 6.38 (D119), asked on 2026-10-09: "When available, I'd like INTERCEPTORS to be used instead of PROBES." C39
        // sells one for more than either shipyard asks for a probe.
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645), ("SHIP_LIGHT_SHUTTLE", 117_273)),
            Shipyard("X1-DC53-C39", ("SHIP_PROBE", 103_729), ("SHIP_INTERCEPTOR", 140_000)),
            Shipyard(H52, ("SHIP_MINING_DRONE", 48_328)),
        ]);

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_INTERCEPTOR", "X1-DC53-C39", 140_000));
        await _purchases.Received(1).TryPurchaseAsync("SHIP_INTERCEPTOR", "X1-DC53-C39", Arg.Any<CancellationToken>());
        await _purchases.DidNotReceive().TryPurchaseAsync("SHIP_PROBE", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _state!.NextProbeShipType.Should().Be("SHIP_INTERCEPTOR");
        _state.NextProbeShipyard.Should().Be("X1-DC53-C39");
    }

    [Fact]
    public async Task AnInterceptorTheShipyardHasScarce_LeavesTheProbeToBeBought()
    {
        // D119 with D121: "As with the other ships, do not buy INTERCEPTORS if the supply is SCARCE."
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645)),
            Shipyard("X1-DC53-C39", ("SHIP_PROBE", 103_729)) with
            {
                ShipTypes = ["SHIP_PROBE", "SHIP_INTERCEPTOR"],
                Ships =
                [
                    new ShipyardShipDto { Type = "SHIP_PROBE", PurchasePrice = 103_729 },
                    new ShipyardShipDto { Type = "SHIP_INTERCEPTOR", PurchasePrice = 140_000, Supply = "SCARCE" },
                ],
            },
        ]);

        await RunAsync();

        await _purchases.Received(1).TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>());
        await _purchases.DidNotReceive().TryPurchaseAsync("SHIP_INTERCEPTOR", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _state!.NextProbeShipType.Should().Be("SHIP_PROBE");
    }

    [Fact]
    public async Task AnInterceptor_IsFlownAsAProbe()
    {
        // D119: an interceptor does a probe's work. As bought, before a restart caches its frame; it sits where the starting
        // probe does in TheStartingProbe_IsAProbe_AndFliesToTheMarketWhosePricesNeedItMost, and goes where it went.
        Fleet(CommandShip(), new ShipModel("SPECTER-9", SystemSymbol, H52, "DOCKED", "CRUISE", 100, 100, ShipType: "SHIP_INTERCEPTOR"), Drone());

        await RunAsync();

        _activeGoals["SPECTER-9"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(A1);
        _state!.Probes.Should().Be(1);
    }

    [Fact]
    public async Task AProbe_IsANeedInTheOrderShipsAreBoughtIn_AndWaitsWhileSomethingComesFirst()
    {
        // Slice 6.10b (D43): the contract's drone, a surveyor, a drone for each scarce mineral and the cargo ships of
        // Trade.ShipPurchases come first; the probes still fly.
        _order.Allows = false;

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", A2, 81_645));
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _state!.Purchase.Should().Be(ProbePurchaseStatus.WaitingForAnotherPurchase);
        _activeGoals["SPECTER-2"].Should().BeOfType<DeployProbeGoal>();
        _log.Journal.Should().NotContain(entry => entry.EventKind == "PlanBlocked");
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
        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(PurchaseNeed.None, "nothing after the probes waits for them");
        _state.Systems.Should().ContainSingle().Which.Markets.Select(market => market.ProbeSymbol).Should().Equal("SPECTER-5", "SPECTER-2");
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
        _state.Systems.Should().ContainSingle().Which.Markets.Select(market => (market.WaypointSymbol, market.ProbeSymbol, market.WatchedByShip)).Should().Equal(
            (A1, "SPECTER-2", false),
            (A2, string.Empty, false),
            (H51, string.Empty, false),
            (H52, string.Empty, false),
            (J58, string.Empty, false),
            (XB5C, string.Empty, true));
        _state.Systems[0].Markets.Single(market => market.WaypointSymbol == H51).DueAt.Should().BeCloseTo(_now.AddMinutes(-25), TimeSpan.FromSeconds(1));
        _state.Systems[0].Markets.Single(market => market.WaypointSymbol == A1).DueAt.Should().Be(default, "a watched market's time changes with every refresh");
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "PlanStarted")
            .Which.Message.Should().Be("PlanStarted: ProbeDeployment plan for system X1-DC53: 1 probes for 6 markets.");
    }

    [Fact]
    public async Task WithAProbeAtEveryHomeMarket_ItBuysOneForTheNearestSystemAbroad_AndSendsItThere()
    {
        // Slice 6.28 (D97): before it, the plan counted home's markets only, bought nothing more and left X1-KR90 unwatched.
        // No probe of ours is in X1-KR90 yet, so its shipyard can't sell one (D30): the probe is bought at home, with the
        // jump's antimatter (5,000 at home's gate) counted, and flies to X1-KR90's shipyard K2, the market it sees first since
        // slice 6.32 (D109; before, the gate, where it came in).
        Abroad();
        _purchases.TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>()).Returns(new ShipPurchaseResult
        {
            IsSuccess = true,
            EstimatedCost = 81_645,
            ActualCost = 81_645,
            PurchasedShip = Probe("SPECTER-7", A2),
        });

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", A2, 81_645));
        _activeGoals["SPECTER-7"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(Kr90K2);
        _activeGoals.Should().ContainSingle("home's probes stay at their markets");
        _state!.Purchase.Should().Be(ProbePurchaseStatus.Bought);
        _state.NextProbeSystem.Should().Be(Kr90);
        _state.NextProbeAntimatter.Should().Be(5_000);
        _state.Systems.Select(system => (system.SystemSymbol, system.Jumps, system.InTradeReach, system.Probes)).Should().Equal(
            (SystemSymbol, 0, true, 3),
            (Kr90, 1, true, 1),
            (Mt49, 2, true, 0));
        _log.Entries.Should().Contain(entry => entry.Message == "Probe plan: probe SPECTER-7, bought in X1-DC53, flies to X1-KR90-K2 in X1-KR90, 1 jumps from home, which has 0 probes for 3 markets.");
    }

    [Fact]
    public async Task AProbeParksAtItsSystemsShipyard_AndTheOthersRoam()
    {
        // Slice 6.32, D110: "Stays parked". Before, SPECTER-2 left the shipyard H52 for the staler markets; now SPECTER-5,
        // at A2, roams to A1, an hour old.
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
            [.. DefaultWaypoints().Select(waypoint => waypoint.Symbol == H52 ? waypoint with { HasShipyard = true } : waypoint)]);
        Fleet(StartingProbe(), Probe("SPECTER-5", A2));

        await RunAsync();

        _activeGoals.Should().NotContainKey("SPECTER-2");
        _activeGoals["SPECTER-5"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(A1);
        _state!.Systems.Single().Markets.Single(market => market.WaypointSymbol == H52)
            .Should().BeEquivalentTo(new { Shipyard = ShipyardKind.Shipyard, ProbeSymbol = "SPECTER-2" });
    }

    [Fact]
    public async Task AShipyardThatSellsExplorers_GetsTheNextProbe_BeforeTheMarketsOfANearerSystem()
    {
        // Slice 6.32, D109: "with shipyards with explorer ships being even higher priority than that". X1-MT49's M1 sells
        // SHIP_EXPLORER, 2 jumps from home; X1-KR90, 1 jump, has three markets without a probe and came first before. Only home
        // sells probes here: the probe is bought there, with both jumps' antimatter, and flies to M1.
        Abroad();
        ShipyardAtM1(explorer: true);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645)),
            Shipyard(Kr90K2, ("SHIP_MINING_DRONE", 48_000)) with { SystemSymbol = Kr90 },
            Shipyard(Mt49M1, ("SHIP_EXPLORER", 702_315)) with { SystemSymbol = Mt49 },
        ]);
        _purchases.TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>()).Returns(new ShipPurchaseResult
        {
            IsSuccess = true,
            EstimatedCost = 81_645,
            ActualCost = 81_645,
            PurchasedShip = Probe("SPECTER-7", A2),
        });

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", A2, 81_645));
        _activeGoals["SPECTER-7"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(Mt49M1);
        _state!.NextProbeSystem.Should().Be(Mt49);
        _state.NextProbeFor.Should().Be(ShipyardKind.Explorer);
        _state.NextProbeAntimatter.Should().Be(9_000);
        _state.Systems.Single(system => system.SystemSymbol == Mt49).Markets.Single(market => market.WaypointSymbol == Mt49M1)
            .Shipyard.Should().Be(ShipyardKind.Explorer);
    }

    [Fact]
    public async Task AShipyardGetsItsProbe_BeforeTheMarketsOfANearerSystem()
    {
        // Slice 6.32, D109: "Across systems". X1-KR90's shipyard K2 has its probe, SPECTER-7, and two markets without one; X1-MT49,
        // a jump further, has a shipyard, M1, without one. K2 sells probes now that SPECTER-7 is there.
        Abroad(Probe("SPECTER-7", Kr90K2) with { SystemSymbol = Kr90 });
        ShipyardAtM1(explorer: false);

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", Kr90K2, 24_000));
        _state!.NextProbeSystem.Should().Be(Mt49);
        _state.NextProbeFor.Should().Be(ShipyardKind.Shipyard);
        _activeGoals.Should().NotContainKey("SPECTER-7", "it stays parked at K2 (D110)");
    }

    [Theory]
    [InlineData(true, PurchaseTier.Probes)]
    [InlineData(false, PurchaseTier.FarProbes)]
    public async Task AShipyardBeyondTheTradeReach_GetsItsProbeWithTheProbesInReach_OnlyIfItSellsExplorers(bool explorer, PurchaseTier tier)
    {
        // Slice 6.32, D111: "Probe tier". With Trade.MaxHaulDistance 1, X1-MT49, 2 jumps away, is beyond the trade reach; the
        // explore plan buys explorers at any shipyard the gates reach. X1-KR90 has its three probes, and K2 sells probes.
        Abroad(Probe("SPECTER-7", Kr90Gate) with { SystemSymbol = Kr90 }, Probe("SPECTER-8", Kr90K1) with { SystemSymbol = Kr90 }, Probe("SPECTER-9", Kr90K2) with { SystemSymbol = Kr90 });
        ShipyardAtM1(explorer);
        _settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, Arg.Any<CancellationToken>()).Returns(1);

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(tier, "SHIP_PROBE", Kr90K2, 24_000));
        _state!.NextProbeSystem.Should().Be(Mt49);
    }

    [Fact]
    public async Task OnceAProbeIsInASystem_TheRestAreBoughtThere_WhereTheyCostLeast()
    {
        // D97: X1-KR90's K2 sells probes for 24,000, home's A2 for 81,645 and a jump; SPECTER-7, in X1-KR90 now, answers the
        // call there (D30).
        Abroad(Probe("SPECTER-7", Kr90Gate) with { SystemSymbol = Kr90 });
        _purchases.TryPurchaseAsync("SHIP_PROBE", Kr90K2, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _calls.Call(Kr90K2, "SHIP_PROBE", TimeProvider.System.GetUtcNow());
            return new ShipPurchaseResult { Failure = ShipPurchaseFailure.NoShipAtShipyard, EstimatedCost = 24_000 };
        });

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", Kr90K2, 24_000));
        _activeGoals["SPECTER-7"].Should().BeEquivalentTo(new { TargetWaypointSymbol = Kr90K2, ForPurchase = true });
        _state!.NextProbeAntimatter.Should().Be(0);
    }

    [Fact]
    public async Task ASystemBeyondTheTradeReach_GetsItsProbesLast_AfterTheDronesAndCargoShips()
    {
        // D97: with Trade.MaxHaulDistance 1, X1-MT49, 2 jumps away, is beyond the trade reach. X1-KR90 has its three.
        Abroad(Probe("SPECTER-7", Kr90Gate) with { SystemSymbol = Kr90 }, Probe("SPECTER-8", Kr90K1) with { SystemSymbol = Kr90 }, Probe("SPECTER-9", Kr90K2) with { SystemSymbol = Kr90 });
        _settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, Arg.Any<CancellationToken>()).Returns(1);

        await RunAsync();

        // K2 for 24,000 and X1-KR90's jump (4,000) beats home's A2 for 81,645 and two jumps.
        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.FarProbes, "SHIP_PROBE", Kr90K2, 24_000));
        _state!.NextProbeSystem.Should().Be(Mt49);
        _state.NextProbeAntimatter.Should().Be(4_000);
        _state.Systems.Single(system => system.SystemSymbol == Mt49).InTradeReach.Should().BeFalse();
    }

    [Fact]
    public async Task NoProbeIsBoughtWhereProbesAreScarce_AtHomeToo()
    {
        // D97: "It should also check whether the probes are in SCARCE supply and not buy them if they are." Home has a market
        // without a probe, and only A2 sells them, at SCARCE.
        Abroad();
        Fleet(StartingProbe(), Probe("SPECTER-5", A2));
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Shipyard(A2, ("SHIP_PROBE", 81_645)) with
        {
            Ships = [new ShipyardShipDto { Type = "SHIP_PROBE", PurchasePrice = 81_645, Supply = "SCARCE" }],
        }]);

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(PurchaseNeed.None);
        _state!.Purchase.Should().Be(ProbePurchaseStatus.ShipyardsScarce);
    }

    [Fact]
    public async Task ASpareProbe_GoesToASystemShortOfOne_BeforeAProbeIsBoughtForIt()
    {
        // B69, slice 6.28: home has four probes for three markets, SPECTER-8 a second at A2; X1-KR90 two for three, and no
        // way to X1-MT49.
        Abroad(Probe("SPECTER-8", A2), Probe("SPECTER-10", Kr90K1) with { SystemSymbol = Kr90 }, Probe("SPECTER-11", Kr90K2) with { SystemSymbol = Kr90 });
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(Network(mt49Connected: false));

        await RunAsync();

        _activeGoals["SPECTER-8"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(Kr90Gate);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _state!.Purchase.Should().Be(ProbePurchaseStatus.EveryMarketHasOne);
        _state.Systems.Select(system => system.Probes).Should().Equal(3, 3);
    }

    [Fact]
    public async Task NoProbeIsSentAbroad_WhereTheJumpWouldLeaveLessThanTheCreditFloor()
    {
        // D63: 64,000 less the jump's 5,000 is under the floor of 60,000: the spare stays, and no probe is bought for X1-KR90.
        Abroad(Probe("SPECTER-8", A2));
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("SPECTER", null, A1, 64_000, "COBALT", 3));
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);

        await RunAsync();

        _activeGoals.Should().NotContainKey("SPECTER-8");
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _state!.Purchase.Should().Be(ProbePurchaseStatus.NoShipyardSellsProbes);
    }

    [Fact]
    public async Task AProbeOnItsWayAbroad_CountsForThatSystem_AndHoldsItsMarketThere()
    {
        // B15 across systems: SPECTER-7 is still at home, in flight to home's gate, on its way to X1-KR90's K1.
        var leaving = Probe("SPECTER-7", A2) with { Status = "IN_TRANSIT", DestWaypointSymbol = I55, ArrivesAt = _now.AddMinutes(2) };
        _activeGoals["SPECTER-7"] = new DeployProbeGoal { TargetWaypointSymbol = Kr90K1 };
        Abroad(leaving);

        await RunAsync();

        _state!.Systems.Select(system => system.Probes).Should().Equal(3, 1, 0);
        _state.Systems[1].Markets.Single(market => market.WaypointSymbol == Kr90K1).ProbeSymbol.Should().Be("SPECTER-7");
        _activeGoals["SPECTER-7"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().Be(Kr90K1);
    }

    [Fact]
    public async Task WhileAProbeIsOnItsWay_TheSystemsOtherProbesWaitToBeBoughtWhereItArrives()
    {
        // B72: X1-KR90's first probe, SPECTER-7, is still at home on its way there. Its K2 sells probes for 24,000, home's A2
        // for 81,645 and a jump; until SPECTER-7 arrives K2 can't sell (D30), and the plan bought X1-KR90's other probes at
        // A2, one a pass, then X1-MT49's, which K2 would sell for 28,000 with the jump. It waits for SPECTER-7 instead.
        var leaving = Probe("SPECTER-7", A2) with { Status = "IN_TRANSIT", DestWaypointSymbol = I55, ArrivesAt = _now.AddMinutes(2) };
        _activeGoals["SPECTER-7"] = new DeployProbeGoal { TargetWaypointSymbol = Kr90K1 };
        Abroad(leaving);

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(PurchaseNeed.None);
        _state!.Purchase.Should().Be(ProbePurchaseStatus.WaitingForAProbeToArrive);
        _state.NextProbeSystem.Should().Be(Kr90);
        _state.NextProbeShipyard.Should().Be(Kr90K2);
        _state.NextProbePrice.Should().Be(24_000);
    }

    [Fact]
    public async Task ASystemsFirstProbe_GoesWhereItsProbesCostLeast_ThoughThatIsAnotherSystem()
    {
        // B72: X1-MT49's shipyard M1 sells probes for 10,000, the cheapest for X1-KR90 too, whose own K2 can't sell before a
        // probe of ours is there (D30). So the first probe goes to X1-MT49, bought at home, the only shipyard that can sell
        // one now, and X1-KR90's are bought at M1 once it is there.
        Abroad();
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645)),
            Shipyard(Kr90K2, ("SHIP_PROBE", 24_000)) with { SystemSymbol = Kr90 },
            Shipyard("X1-MT49-M1", ("SHIP_PROBE", 10_000)) with { SystemSymbol = Mt49 },
        ]);
        _purchases.TryPurchaseAsync("SHIP_PROBE", A2, Arg.Any<CancellationToken>()).Returns(new ShipPurchaseResult
        {
            IsSuccess = true,
            EstimatedCost = 81_645,
            ActualCost = 81_645,
            PurchasedShip = Probe("SPECTER-7", A2),
        });

        await RunAsync();

        _order.Of(AutomationPlan.ProbeDeployment).Should().Be(new PurchaseNeed(PurchaseTier.Probes, "SHIP_PROBE", A2, 81_645));
        _activeGoals["SPECTER-7"].Should().BeOfType<DeployProbeGoal>().Which.TargetWaypointSymbol.Should().StartWith(Mt49);
        _state!.NextProbeSystem.Should().Be(Mt49);
        _state.NextProbeAntimatter.Should().Be(9_000);
    }

    /// <summary>
    /// Slice 6.28: home X1-DC53 has three markets, A2 (a shipyard), H52 and its gate I55, each with a probe; its gate connects
    /// to X1-KR90's, explored (its gate G, K1, and K2, a shipyard that sells probes for 24,000), and that to X1-MT49's (its gate
    /// and M1). Jumps cost 5,000 from home's gate and 4,000 from X1-KR90's. No market abroad was ever seen.
    /// </summary>
    private void Abroad(params ShipModel[] more)
    {
        _waypoints.GetBySystemAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(
        [
            Waypoint(A2, "MOON", 21, 16, shipyard: true),
            Waypoint(H52, "MOON", -18, 40, shipyard: true),
            Waypoint(I55, "JUMP_GATE", 272, -358),
        ]);
        _waypoints.GetBySystemAsync(Kr90, Arg.Any<CancellationToken>()).Returns(
        [
            WaypointIn(Kr90, Kr90Gate, "JUMP_GATE", 0, 0),
            WaypointIn(Kr90, Kr90K1, "PLANET", 30, 0),
            WaypointIn(Kr90, Kr90K2, "MOON", 100, 0, shipyard: true),
        ]);
        _waypoints.GetBySystemAsync(Mt49, Arg.Any<CancellationToken>()).Returns(
        [
            WaypointIn(Mt49, Mt49Gate, "JUMP_GATE", 0, 0),
            WaypointIn(Mt49, Mt49M1, "PLANET", 20, 20),
        ]);
        LastSeen((A2, 1), (H52, 1), (I55, 1));
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new MarketSnapshot(I55, SystemSymbol, [new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", 5_000, 4_800, 10, "MODERATE")], [], [], ["ANTIMATTER"]),
            new MarketSnapshot(Kr90Gate, Kr90, [new TradeGoodSnapshot("ANTIMATTER", "EXCHANGE", 4_000, 3_800, 10, "MODERATE")], [], [], ["ANTIMATTER"]),
        ]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645)),
            Shipyard(Kr90K2, ("SHIP_PROBE", 24_000)) with { SystemSymbol = Kr90 },
        ]);
        _gates.ReadAsync(Arg.Any<CancellationToken>()).Returns(Network(mt49Connected: true));
        Fleet([StartingProbe(), Probe("SPECTER-5", A2), Probe("SPECTER-6", I55), .. more]);
    }

    /// <summary>
    /// Slice 6.32: X1-MT49's M1 is a shipyard, with <paramref name="explorer"/> one that sells SHIP_EXPLORER (else light
    /// shuttles); home's A2 and X1-KR90's K2 sell probes, as in <see cref="Abroad"/>.
    /// </summary>
    private void ShipyardAtM1(bool explorer)
    {
        _waypoints.GetBySystemAsync(Mt49, Arg.Any<CancellationToken>()).Returns(
        [
            WaypointIn(Mt49, Mt49Gate, "JUMP_GATE", 0, 0),
            WaypointIn(Mt49, Mt49M1, "PLANET", 20, 20, shipyard: true),
        ]);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Shipyard(A2, ("SHIP_PROBE", 81_645)),
            Shipyard(Kr90K2, ("SHIP_PROBE", 24_000)) with { SystemSymbol = Kr90 },
            Shipyard(Mt49M1, explorer ? ("SHIP_EXPLORER", 702_315) : ("SHIP_LIGHT_SHUTTLE", 117_273)) with { SystemSymbol = Mt49 },
        ]);
    }

    /// <summary>X1-DC53's markets as the tests before slice 6.32 knew them: A2 and H52 without their shipyard trait.</summary>
    private static List<WaypointCacheModel> DefaultWaypoints() =>
    [
        Waypoint(A1, "PLANET", 21, 16),
        Waypoint(A2, "MOON", 21, 16),
        Waypoint(H51, "PLANET", -18, 40),
        Waypoint(H52, "MOON", -18, 40),
        Waypoint(XB5C, "ENGINEERED_ASTEROID", -15, 21),
        Waypoint(J58, "ASTEROID_BASE", 435, -572),
    ];

    private ExplorePlanState Network(bool mt49Connected) => new()
    {
        ShipSymbol = "SPECTER-1",
        HomeSystemSymbol = SystemSymbol,
        Status = ExploreStatus.Exploring,
        UpdatedAt = _now,
        Systems =
        [
            new KnownSystem { SystemSymbol = SystemSymbol, GateWaypointSymbol = I55, Gate = GateState.Active, Connections = [Kr90Gate], ExploredAt = _now },
            new KnownSystem { SystemSymbol = Kr90, GateWaypointSymbol = Kr90Gate, Gate = GateState.Active, Connections = mt49Connected ? [I55, Mt49Gate] : [I55], ExploredAt = _now },
            new KnownSystem { SystemSymbol = Mt49, GateWaypointSymbol = Mt49Gate, Gate = GateState.Active, Connections = [Kr90Gate], ExploredAt = _now },
        ],
    };

    private static WaypointCacheModel WaypointIn(string system, string symbol, string type, int x, int y, bool shipyard = false)
        => new(symbol, system, type, x, y, HasMarket: true, HasShipyard: shipyard, DateTimeOffset.UnixEpoch);

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
                _order,
                _gates,
                _log.For<ProbeDeploymentPlanService>())
            .EnsureBootstrappedAsync();
}

using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
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
/// Slice 6.30 (D98, D99, D102). Asked on 2026-10-06: "I'd like more explorers to be added when there are more systems to be
/// discovered. Maybe 1 explorer for every 10 undiscovered systems?", with "Reachable, round up", "Cap as a setting", "First
/// before probes, rest last" and "Explorers can trade with 40 cargo space, so they can trade at the location they are at until
/// a new unexplored location comes up." Since slice 6.33 (D113) the rest come before the probes too. The systems are as the explore plan knew them that day, shortened: home X1-FJ91, whose
/// built gate connects to X1-GT9, explored, where X1-GT9-AE7B sells SHIP_EXPLORER for 702,315, to eleven systems not explored
/// yet behind built gates, and to X1-XJ90, whose gate is still under construction. The plan runs pass after pass over the cache
/// the real repositories keep; the API, the purchases and the order ships are bought in are fakes.
/// </summary>
public sealed class ExplorersTests
{
    private const string CommandShip = "SPECTER-1";
    private const string Explorer = "SPECTER-50";
    private const string SecondExplorer = "SPECTER-51";
    private const string Home = "X1-FJ91";
    private const string HomeGate = "X1-FJ91-I64";
    private const string Gt9 = "X1-GT9";
    private const string Gt9Gate = "X1-GT9-E10Z";
    private const string Shipyard = "X1-GT9-AE7B";
    private const string Xj90Gate = "X1-XJ90-I59";
    private const long Price = 702_315;

    /// <summary>The eleven systems not explored yet, each one jump from home.</summary>
    private static readonly string[] Unexplored = [.. Enumerable.Range(1, 11).Select(index => $"X1-S{index:00}")];

    private readonly string _database = Guid.NewGuid().ToString();
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly IPurchaseOrder _order = Substitute.For<IPurchaseOrder>();
    private readonly JumpRefusals _refusals = new();
    private readonly LogRecorder _log = new();

    public ExplorersTests()
    {
        _settings.GetAsync<long>(CreditReserve.FloorSetting, Arg.Any<CancellationToken>()).Returns(60_000L);
        ProbePlanOn(true);
        Settings(perExplorer: 10, cap: 5);
        OrderLets(PurchaseTier.Explorer);
        PurchaseFails(ShipPurchaseFailure.NoShipAtShipyard);
    }

    [Theory]
    [InlineData(10, 5, 2)]
    [InlineData(10, 1, 1)]
    [InlineData(5, 0, 3)]
    [InlineData(20, 5, 1)]
    [InlineData(0, 5, 0)]
    public async Task OneExplorerIsWanted_ForEvery10SystemsLeft_RoundedUp_AtMostTheCap(int perExplorer, int cap, int wanted)
    {
        // Eleven systems left behind built gates; X1-XJ90's gate is unbuilt, so it doesn't count yet.
        Settings(perExplorer, cap);
        await SeedAsync();

        await PassAsync();

        var state = await StateAsync();
        state.SystemsLeft.Should().Be(11);
        state.ExplorersWanted.Should().Be(wanted);
        if (wanted == 0)
        {
            await _order.DidNotReceive().ReportAsync(AutomationPlan.Explore, Arg.Is<PurchaseNeed>(need => need.Tier != PurchaseTier.None), Arg.Any<CancellationToken>());
            await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!);
        }
    }

    [Fact]
    public async Task TheFirstExplorer_StandsBeforeTheProbes_AndTheCommandShipFetchesIt()
    {
        // D98: "The command ship, out exploring, goes to the shipyard and buys one". The API sells a ship only where one of
        // ours is (D30): the purchase calls for one, and the command ship answers it.
        await SeedAsync();

        await PassAsync();

        await _order.Received().ReportAsync(
            AutomationPlan.Explore,
            Arg.Is<PurchaseNeed>(need => need.Tier == PurchaseTier.Explorer && need.ShipType == "SHIP_EXPLORER" && need.ShipyardWaypointSymbol == Shipyard && need.Price == Price),
            Arg.Any<CancellationToken>());
        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { TargetWaypointSymbol = Shipyard });
        (await GoalAsync(CommandShip)).Should().BeOfType<MoveToWaypointGoal>();
        (await AssignmentAsync(CommandShip)).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });
        var state = await StateAsync();
        state.Status.Should().Be(ExploreStatus.FetchingExplorer);
        state.TargetSystemSymbol.Should().Be(Gt9);
        state.Purchase.Should().BeEquivalentTo(new ExplorerPurchaseState
        {
            Status = ExplorerPurchaseStatus.CommandShipFetchesIt,
            Tier = PurchaseTier.Explorer,
            ShipyardWaypointSymbol = Shipyard,
            Price = Price,
        });
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.PlanStarted).Which.Message.Should().Contain(Shipyard);
    }

    [Fact]
    public async Task AtTheShipyard_TheExplorerIsBought_AndTheCommandShipComesHomeToWork()
    {
        // One explorer wanted: since slice 6.33 (D113) a second would come next, before the probes, and with no probe of ours in
        // X1-GT9 the command ship would fetch that one too (D108).
        Settings(perExplorer: 10, cap: 1);
        await SeedAsync();
        await PassAsync();

        // The command ship flew there (D30), and the purchase goes through.
        await MoveAsync(CommandShip, Gt9, Shipyard);
        PurchaseSucceeds();
        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.Bought);
        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = HomeGate });
        (await StateAsync()).Status.Should().Be(ExploreStatus.Returning);

        // The explorer explores from where it was bought: the nearest system not explored, through home's gate.
        await AddShipAsync(Explorer, Gt9, Shipyard, "SHIP_EXPLORER");
        PurchaseFails(ShipPurchaseFailure.NoShipAtShipyard);
        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = HomeGate });
        (await AssignmentAsync(Explorer)).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });
        (await StateAsync()).Explorers.Should().ContainSingle().Which.Should().BeEquivalentTo(new ExploringShip
        {
            ShipSymbol = Explorer,
            Status = ExploreStatus.Exploring,
            TargetSystemSymbol = "X1-S01",
        });

        // Home: the command ship is released to the other plans (D60).
        await MoveAsync(CommandShip, Home, HomeGate);
        await PassAsync();

        (await GoalAsync(CommandShip)).Should().BeNull();
        (await AssignmentAsync(CommandShip))!.CompletedAt.Should().NotBeNull();
        (await StateAsync()).Status.Should().Be(ExploreStatus.Done);
        _log.Journal.Should().Contain(line => line.EventKind == JournalEvents.PlanCompleted && line.Message.Contains(CommandShip, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhileTheCreditsAreShort_TheCommandShipExploresOn_ButOnceOnItsWay_ItWaitsAtTheShipyard()
    {
        await SeedAsync();
        PurchaseFails(ShipPurchaseFailure.OverBudget);

        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.WaitingForCredits);
        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = "X1-S01-G" });

        // Sent once the credits allowed it; they ran short again on its way. At the shipyard it waits for them.
        await ClearGoalAsync(CommandShip);
        await MoveAsync(CommandShip, Home, "X1-FJ91-A1");
        PurchaseFails(ShipPurchaseFailure.NoShipAtShipyard);
        await PassAsync();
        await MoveAsync(CommandShip, Gt9, Shipyard);
        PurchaseFails(ShipPurchaseFailure.OverBudget);
        await PassesAsync(3);

        (await GoalAsync(CommandShip)).Should().BeNull();
        var state = await StateAsync();
        state.Status.Should().Be(ExploreStatus.FetchingExplorer);
        state.Purchase.Status.Should().Be(ExplorerPurchaseStatus.WaitingForCredits);
    }

    [Fact]
    public async Task WhileNoWayToTheShipyardIsKnown_TheCommandShipExploresOn()
    {
        // X1-GT9's gate refused a jump within the hour (JumpRefusals, slice 6.28): a flight there would find no way and end at
        // once, on every pass. The command ship explores on, and fetches the explorer once the gate is usable again.
        await SeedAsync();
        _refusals.Record(Gt9Gate, _now.AddMinutes(-10));

        await PassAsync();

        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = "X1-S01-G" });
        (await StateAsync()).Status.Should().Be(ExploreStatus.Exploring);
    }

    [Fact]
    public async Task AFurtherExplorer_ComesBeforeTheProbes_AndTheCommandShipFetchesIt_WhenNoProbeIsThere()
    {
        // D108: "Can we set up the command ship to go to that location if there isn't a probe there", the location being "where
        // an explorer ship is supposed to be bought if it's in the purchase order and enough credits are available". It counts in
        // the order though none of our ships is in X1-GT9 (D102 waited for one there): the command ship can meet it. Slice 6.33
        // (D113), asked on 2026-10-07: "I'd like Explorers (order 10) to go in front of probes (order 8)": the first explorer's
        // place, no longer after the drones and cargo ships that take turns.
        await SeedAsync();
        await AddShipAsync(Explorer, "X1-S01", "X1-S01-G", "SHIP_EXPLORER");

        await PassAsync();

        await _order.Received().ReportAsync(
            AutomationPlan.Explore,
            Arg.Is<PurchaseNeed>(need => need.Tier == PurchaseTier.Explorer && need.ShipyardWaypointSymbol == Shipyard),
            Arg.Any<CancellationToken>());
        (await GoalAsync(CommandShip)).Should().BeOfType<MoveToWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(Shipyard);
        (await AssignmentAsync(CommandShip)).Should().BeEquivalentTo(new { AssignmentType = ExplorePlanService.AssignmentType, CompletedAt = (DateTimeOffset?)null });
        var state = await StateAsync();
        state.Status.Should().Be(ExploreStatus.FetchingExplorer);
        state.Purchase.Should().BeEquivalentTo(new { Status = ExplorerPurchaseStatus.CommandShipFetchesIt, Tier = PurchaseTier.Explorer, ShipyardWaypointSymbol = Shipyard });
        _log.Journal.Should().ContainSingle(line => line.EventKind == JournalEvents.PlanStarted && line.Message.Contains(CommandShip, StringComparison.Ordinal))
            .Which.Message.Should().Contain(Shipyard);

        // At the shipyard the explorer is bought, and the command ship comes home to work (D60).
        await MoveAsync(CommandShip, Gt9, Shipyard);
        PurchaseSucceeds();
        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.Bought);
        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = HomeGate });
        (await StateAsync()).Status.Should().Be(ExploreStatus.Returning);
    }

    [Fact]
    public async Task TheFirstExplorer_IsLeftToAProbeInTheShipyardsSystem_AndTheCommandShipExploresOn()
    {
        // D108: "if there isn't a probe there". The purchase calls for a ship (D30), and the probe plan sends the probe in
        // X1-GT9 to the shipyard: the command ship would fly four times as far for nothing.
        await SeedAsync();
        await AddShipAsync("SPECTER-60", Gt9, "X1-GT9-B1", "SHIP_PROBE");

        await PassAsync();

        await _purchases.Received().TryPurchaseAsync("SHIP_EXPLORER", Shipyard, Arg.Any<CancellationToken>());
        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.WaitingForAShipThere, "the call brings the probe there");
        (await GoalAsync(CommandShip)).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = "X1-S01-G" });
        (await StateAsync()).Status.Should().Be(ExploreStatus.Exploring);
    }

    [Theory]
    [InlineData("X1-GT9-B1", true)]
    [InlineData("X1-S01-M", false)]
    public async Task AProbeOnItsWayToTheShipyardsSystem_AnswersTheCall_ButOneThatOnlyPassesThroughDoesnt(string flightTo, bool answers)
    {
        // A probe counts for the system its flight goes to, as the probe plan counts it (B15): one jumping in from home answers
        // the call once there; one at X1-GT9's gate on its way to X1-S01 doesn't.
        await SeedAsync();
        await AddShipAsync(Explorer, "X1-S01", "X1-S01-G", "SHIP_EXPLORER");
        var (system, waypoint) = answers ? (Home, "X1-FJ91-A1") : (Gt9, Gt9Gate);
        await AddShipAsync("SPECTER-60", system, waypoint, "SHIP_PROBE");
        await SetGoalAsync("SPECTER-60", new DeployProbeGoal { TargetWaypointSymbol = flightTo });

        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(answers ? ExplorerPurchaseStatus.WaitingForAShipThere : ExplorerPurchaseStatus.CommandShipFetchesIt);
        if (answers)
        {
            (await GoalAsync(CommandShip)).Should().BeNull("an explorer explores, and the probe brings the purchase its ship");
        }
        else
        {
            (await GoalAsync(CommandShip)).Should().BeOfType<MoveToWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(Shipyard);
        }
    }

    [Fact]
    public async Task WithTheProbePlanOff_NoProbeAnswersTheCall_SoTheCommandShipFetchesTheExplorer()
    {
        // The probe plan answers the calls (D30); switched off, its probes stay where they are.
        await SeedAsync();
        await AddShipAsync("SPECTER-60", Gt9, "X1-GT9-B1", "SHIP_PROBE");
        ProbePlanOn(false);

        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.CommandShipFetchesIt);
        (await GoalAsync(CommandShip)).Should().BeOfType<MoveToWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(Shipyard);
    }

    [Fact]
    public async Task OnceOnItsWay_TheCommandShipStaysWithTheExplorer_WhileTheOrderHoldsItBack()
    {
        // Read with D108, as D98 waits for the credits: the jump gate's next load came first while it flew there. It waits at
        // the shipyard, where the purchase needs it, rather than flying home and back.
        await SeedAsync();
        await AddShipAsync(Explorer, "X1-S01", "X1-S01-G", "SHIP_EXPLORER");
        await PassAsync();

        await MoveAsync(CommandShip, Gt9, Shipyard);
        OrderLets(PurchaseTier.None);
        await PassesAsync(2);

        (await GoalAsync(CommandShip)).Should().BeNull();
        (await AssignmentAsync(CommandShip))!.CompletedAt.Should().BeNull();
        var state = await StateAsync();
        state.Status.Should().Be(ExploreStatus.FetchingExplorer);
        state.Purchase.Status.Should().Be(ExplorerPurchaseStatus.WaitingForAnotherPurchase);
    }

    [Fact]
    public async Task UntilTheOrderLetsAFurtherExplorerThrough_TheCommandShipKeepsItsWork()
    {
        // D108: "if it's in the purchase order and enough credits are available". At 15:55Z on 2026-10-06 the probes within
        // the trade reach and the drones and cargo ships that take turns came first (D102); since D113 only what comes before
        // the explorers can, such as the jump gate's next load.
        await SeedAsync();
        await AddShipAsync(Explorer, "X1-S01", "X1-S01-G", "SHIP_EXPLORER");
        OrderLets(PurchaseTier.None);

        await PassAsync();

        (await StateAsync()).Purchase.Status.Should().Be(ExplorerPurchaseStatus.WaitingForAnotherPurchase);
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!);
        (await GoalAsync(CommandShip)).Should().BeNull();
        (await AssignmentAsync(CommandShip)).Should().BeNull();
    }

    [Theory]
    [InlineData(1, "X1-S01", HomeGate)]
    [InlineData(5, "X1-FAR", "X1-FAR-G")]
    public async Task AnExplorer_TakesTheSystemsWithinTheTradeReachFirst_ThoughOneBeyondIsNearerToIt(int reach, string target, string firstJump)
    {
        // D103: "Reach first, then nearest", the reach being Trade.MaxHaulDistance. X1-FAR hangs off X1-GT9, 2 jumps from home:
        // the explorer at X1-GT9's gate is 1 jump from it, and 2 from the systems one jump from home. Within a reach of 1 they
        // come first; within one of 5 all are, and the nearest goes first.
        await SeedAsync();
        await AddSystemAsync("X1-FAR", from: Gt9);
        await AddShipAsync(Explorer, Gt9, Gt9Gate, "SHIP_EXPLORER");
        _settings.GetAsync<int>(TradeContextReader.MaxHaulDistanceSetting, Arg.Any<CancellationToken>()).Returns(reach);

        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = firstJump });
        (await StateAsync()).Explorers.Single().TargetSystemSymbol.Should().Be(target);
    }

    [Fact]
    public async Task EachExplorer_TakesTheNearestSystemNoOtherHasTaken_AndTheCommandShipStaysHome()
    {
        await SeedAsync();
        await AddShipAsync(Explorer, Home, HomeGate, "SHIP_EXPLORER");
        await AddShipAsync(SecondExplorer, Home, HomeGate, "EXPLORER");

        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = "X1-S01-G" });
        (await GoalAsync(SecondExplorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = HomeGate, DestinationGateWaypointSymbol = "X1-S02-G" });
        (await StateAsync()).Explorers.Select(explorer => (explorer.ShipSymbol, explorer.TargetSystemSymbol))
            .Should().Equal((Explorer, "X1-S01"), (SecondExplorer, "X1-S02"));
        (await GoalAsync(CommandShip)).Should().BeNull();
        (await AssignmentAsync(CommandShip)).Should().BeNull("an explorer explores: the command ship keeps its work at home");
        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!);
    }

    [Fact]
    public async Task ACommandShipThatTradesAbroad_IsNotTakenHome_WithNothingLeftToExplore()
    {
        // Since slice 6.29 a ship whose role is trading takes routes across systems, and "a trader stays where its last sale
        // leaves it" (D96). The plan brings home a command ship that explored (D60); one that was released and trades abroad
        // it took home again after every trip there.
        await SeedAsync(unexplored: 0);
        await MoveAsync(CommandShip, Gt9, "X1-GT9-B1");

        await PassesAsync(2);

        (await GoalAsync(CommandShip)).Should().BeNull();
        (await AssignmentAsync(CommandShip)).Should().BeNull("it trades from where it is");
    }

    [Fact]
    public async Task AnExplorerWithNothingLeftToExplore_TradesWhereItIs_UntilASystemTurnsUp()
    {
        // D102: "they can trade at the location they are at until a new unexplored location comes up."
        await SeedAsync(unexplored: 0);
        await AddShipAsync(Explorer, Gt9, Gt9Gate, "SHIP_EXPLORER");
        await TakeAsync(Explorer);

        await PassAsync();

        (await AssignmentAsync(Explorer))!.CompletedAt.Should().NotBeNull("it is released where it is");
        (await StateAsync()).Explorers.Should().ContainSingle().Which.Should().BeEquivalentTo(new ExploringShip
        {
            ShipSymbol = Explorer,
            Status = ExploreStatus.Waiting,
            Reason = "nothing_to_explore",
        });
        _log.Journal.Should().Contain(line => line.EventKind == JournalEvents.PlanCompleted && line.Message.Contains(Explorer, StringComparison.Ordinal));

        // The trading plan gives it a route; meanwhile a system turns up behind X1-GT9's gate.
        await SetGoalAsync(Explorer, new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = "X1-GT9-B1", SellWaypointSymbol = "X1-FJ91-A1" });
        await AddSystemAsync("X1-S12", from: Gt9);
        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeOfType<TradeBetweenMarketsGoal>("its trip ends first");

        // The trip has ended, back in X1-GT9: the plan takes it again.
        await ClearGoalAsync(Explorer);
        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new { GateWaypointSymbol = Gt9Gate, DestinationGateWaypointSymbol = "X1-S12-G" });
        (await AssignmentAsync(Explorer))!.CompletedAt.Should().BeNull();
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Exploring, TargetSystemSymbol = "X1-S12" });
    }

    [Fact]
    public async Task InASystemNotExplored_TheShipChartsWhatIsUncharted_TheGateFirst_ButNoAsteroidsOrGasGiants()
    {
        // D99: "Every uncharted market or shipyard, I'm not sure if every single asteroid needs to be charted but I don't want
        // my explorer to waste time on that." An uncharted waypoint hides its traits, a marketplace among them.
        await SeedAsync();
        await AddShipAsync(Explorer, "X1-S01", "X1-S01-G", "SHIP_EXPLORER");
        await TakeAsync(Explorer);
        const string Uncharted = """[{"symbol":"UNCHARTED"}]""";
        await using (var db = TestDbContextFactory.Create(_database))
        {
            await new WaypointRepository(db).UpsertRangeAsync(
            [
                new WaypointCacheModel("X1-S01-G", "X1-S01", "JUMP_GATE", 300, 0, HasMarket: false, HasShipyard: false, _now, Uncharted),
                new WaypointCacheModel("X1-S01-ROCK", "X1-S01", "ASTEROID", 290, 0, HasMarket: false, HasShipyard: false, _now, Uncharted),
                new WaypointCacheModel("X1-S01-GAS", "X1-S01", "GAS_GIANT", 250, 0, HasMarket: false, HasShipyard: false, _now, Uncharted),
                new WaypointCacheModel("X1-S01-MOON", "X1-S01", "MOON", 200, 0, HasMarket: true, HasShipyard: false, _now, """[{"symbol":"MARKETPLACE"}]"""),
                new WaypointCacheModel("X1-S01-PLANET", "X1-S01", "PLANET", 0, 0, HasMarket: false, HasShipyard: false, _now, Uncharted),
                new WaypointCacheModel("X1-S01-BARE", "X1-S01", "PLANET", 100, 0, HasMarket: false, HasShipyard: false, _now, """[{"symbol":"BARREN"}]"""),
            ]);
        }

        await PassAsync();

        (await GoalAsync(Explorer)).Should().BeEquivalentTo(new
        {
            SystemSymbol = "X1-S01",
            Stops = new[] { "X1-S01-G", "X1-S01-MOON", "X1-S01-PLANET" },
            Visited = 0,
        });
        (await StateAsync()).Explorers.Single().Should().BeEquivalentTo(new { Status = ExploreStatus.Exploring, TargetSystemSymbol = "X1-S01" });
    }

    private void Settings(int perExplorer, int cap)
    {
        _settings.GetAsync<int>(ExplorePlanService.SystemsPerExplorerSetting, Arg.Any<CancellationToken>()).Returns(perExplorer);
        _settings.GetAsync<int>(ExplorePlanService.MaxExplorersSetting, Arg.Any<CancellationToken>()).Returns(cap);
    }

    /// <summary>Switches the probe plan, which answers a purchase's call for a ship (D30), on or off.</summary>
    private void ProbePlanOn(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.ProbeDeployment), Arg.Any<CancellationToken>()).Returns(on);

    /// <summary>The order ships are bought in lets the explore plan buy a need at <paramref name="tier"/>, and no other.</summary>
    private void OrderLets(PurchaseTier tier)
        => _order.ReportAsync(AutomationPlan.Explore, Arg.Any<PurchaseNeed>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<PurchaseNeed>().Tier == tier);

    private void PurchaseFails(ShipPurchaseFailure failure)
        => _purchases.TryPurchaseAsync("SHIP_EXPLORER", Shipyard, Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { Failure = failure, EstimatedCost = Price });

    private void PurchaseSucceeds()
        => _purchases.TryPurchaseAsync("SHIP_EXPLORER", Shipyard, Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult
            {
                IsSuccess = true,
                EstimatedCost = Price,
                ActualCost = Price,
                PurchasedShip = Ship(Explorer, Gt9, Shipyard, "SHIP_EXPLORER"),
            });

    /// <summary>
    /// The agent with 2,000,000 credits, its command ship docked at home with nothing to do, and the plan's map: home and
    /// X1-GT9 explored, with their waypoints cached and the shipyard's explorer; <paramref name="unexplored"/> systems not
    /// explored behind built gates, one jump from home; X1-XJ90 behind a gate under construction.
    /// </summary>
    private async Task SeedAsync(int unexplored = 11)
    {
        var systems = Unexplored.Take(unexplored).ToList();
        await using var db = TestDbContextFactory.Create(_database);
        await new AgentRepository(db).UpsertAsync(new AgentModel("SPECTER", "account", "X1-FJ91-A1", 2_000_000, "COSMIC", 21));
        await new ShipRepository(db).UpsertAsync(Ship(CommandShip, Home, "X1-FJ91-A1", ExplorePlanService.CommandShipType));
        await new WaypointRepository(db).UpsertRangeAsync(
        [
            new WaypointCacheModel("X1-FJ91-A1", Home, "PLANET", 0, 0, HasMarket: true, HasShipyard: false, _now, """[{"symbol":"MARKETPLACE"}]"""),
            new WaypointCacheModel(HomeGate, Home, "JUMP_GATE", 100, 0, HasMarket: true, HasShipyard: false, _now, """[{"symbol":"MARKETPLACE"}]"""),
            new WaypointCacheModel(Gt9Gate, Gt9, "JUMP_GATE", 0, 0, HasMarket: true, HasShipyard: false, _now, """[{"symbol":"MARKETPLACE"}]"""),
            new WaypointCacheModel(Shipyard, Gt9, "ORBITAL_STATION", 40, 30, HasMarket: true, HasShipyard: true, _now, """[{"symbol":"MARKETPLACE"},{"symbol":"SHIPYARD"}]"""),
            new WaypointCacheModel("X1-GT9-B1", Gt9, "PLANET", -50, 0, HasMarket: true, HasShipyard: false, _now, """[{"symbol":"MARKETPLACE"}]"""),
        ]);
        await new ShipyardRepository(db).UpsertAsync(new ShipyardDataModel(
            Shipyard,
            Gt9,
            """[{"type":"SHIP_EXPLORER"}]""",
            $$"""[{"type":"SHIP_EXPLORER","purchasePrice":{{Price}},"supply":"HIGH","frame":{"fuelCapacity":800},"modules":[{"symbol":"MODULE_CARGO_HOLD_II","capacity":40}]}]"""));
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, new ExplorePlanState
        {
            ShipSymbol = CommandShip,
            HomeSystemSymbol = Home,
            Status = ExploreStatus.Done,
            UpdatedAt = _now,
            Systems =
            [
                Known(Home, HomeGate, explored: true, connections: [Gt9Gate, Xj90Gate, .. systems.Select(system => $"{system}-G")]),
                Known(Gt9, Gt9Gate, explored: true, connections: [HomeGate]),
                Known("X1-XJ90", Xj90Gate, explored: false) with { Gate = GateState.UnderConstruction },
                .. systems.Select(system => Known(system, $"{system}-G", explored: false)),
            ],
        });
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
    private async Task AddSystemAsync(string system, string from)
    {
        var state = await StateAsync();
        state = state with
        {
            Systems =
            [
                .. state.Systems.Select(known => known.SystemSymbol == from ? known with { Connections = [.. known.Connections ?? [], $"{system}-G"] } : known),
                Known(system, $"{system}-G", explored: false),
            ],
        };
        await using var db = TestDbContextFactory.Create(_database);
        await new PlanRepository(db).UpsertAsync(PlanTypes.Explore, state);
    }

    private static ShipModel Ship(string symbol, string system, string waypoint, string type)
        => new(symbol, system, waypoint, "DOCKED", "CRUISE", 800, 800, CargoCapacity: 40, ShipType: type);

    private async Task AddShipAsync(string symbol, string system, string waypoint, string type)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipRepository(db).UpsertAsync(Ship(symbol, system, waypoint, type));
    }

    /// <summary>The ship is at <paramref name="waypoint"/>, docked, its goal ended: a flight or a jump's arrival.</summary>
    private async Task MoveAsync(string ship, string system, string waypoint)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipRepository(db).UpdateNavAsync(ship, new NavModel("DOCKED", system, waypoint, "CRUISE", waypoint, _now.AddSeconds(-1)), null);
        await new ShipGoalRepository(db).ClearActiveGoalAsync(ship);
    }

    /// <summary>The explore plan has the ship already: an open explore assignment.</summary>
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
                _purchases,
                _order,
                _refusals,
                new WarpRefusals(),
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

    private async Task ClearGoalAsync(string ship)
    {
        await using var db = TestDbContextFactory.Create(_database);
        await new ShipGoalRepository(db).ClearActiveGoalAsync(ship);
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

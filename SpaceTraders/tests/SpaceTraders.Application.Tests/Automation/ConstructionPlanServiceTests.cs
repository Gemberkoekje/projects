using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Application.Tests.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.6, asked on 2026-10-04: "Finishing this jump node should be top priority, as it opens up the rest of the game."
/// The ship with the construction role buys the home gate's materials and supplies them; supplying pays nothing, so a load
/// keeps the credit reserve and comes after the cargo ships in the order ships are bought in (D64). A ship that holds what
/// the gate needs takes it there first.
/// </summary>
public sealed class ConstructionPlanServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ITradeContextReader _tradeContexts = Substitute.For<ITradeContextReader>();
    private readonly IConstructionSites _sites = Substitute.For<IConstructionSites>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();
    private readonly OpenPurchaseOrder _order = new();
    private readonly ConstructionRetries _retries = new();
    private readonly LogRecorder _log = new();
    private readonly PassedOverShips _passedOver = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private ConstructionPlanState? _state;

    public ConstructionPlanServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<ConstructionPlanState>(PlanTypes.Construction, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.Construction, Arg.Any<ConstructionPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<ConstructionPlanState>(1));
        _sites.HomeSystemAsync(Arg.Any<CancellationToken>()).Returns(SystemSymbol);
        _sites.NeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns([Site()]);
        PricesAre(Map());
        Spendable(500_000);
    }

    [Fact]
    public async Task AFreeBuilderWithAnEmptyHold_TakesAFullHold_AndHoldsBackWhatItCosts()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler());

        await RunAsync();

        var trip = _activeGoals["SHIP-6"].Should().BeOfType<SupplyConstructionGoal>().Subject;
        (trip.TradeSymbol, trip.BuyWaypointSymbol, trip.ConstructionSiteWaypointSymbol, trip.Units).Should().Be(("FAB_MATS", F49, Gate, 80));
        trip.ReservedCredits.Should().Be(80 * 2_100);
        trip.CargoBought.Should().BeFalse();

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("ConstructionStarted");
        started.Properties["Reason"].Should().Be("purchase");

        // D64: its place in the order ships are bought in, after the cargo ships.
        _order.Of(AutomationPlan.Construction).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Construction && need.ShipType == "FAB_MATS" && need.ShipyardWaypointSymbol == F49);
    }

    [Fact]
    public async Task APurchaseBeforeItInTheOrder_HoldsTheLoadBack()
    {
        // D64: the contract's drone, the surveyors, the drones per scarce mineral and the cargo ships come first.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler());
        _order.Allows = false;

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Waiting.Should().Be(ConstructionPlanService.WaitingForPurchaseOrder);
        _order.Of(AutomationPlan.Construction).Tier.Should().Be(PurchaseTier.Construction);
    }

    [Fact]
    public async Task ALoadThatWouldDipIntoTheCreditReserve_Waits_AndTheProbesWaitBehindIt()
    {
        // 168,000 for the cargo and 532 for fuel; 150,000 above the reserve. The builder trades meanwhile: the trading plan
        // comes next. The need stays at its place in the order, so the probes and further ships wait (D64).
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler());
        Spendable(150_000);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Waiting.Should().Be(ConstructionPlanService.WaitingForCredits);
        _state.ReadyShipSymbols.Should().BeEmpty();
        _order.Of(AutomationPlan.Construction).Should().Match<PurchaseNeed>(need => need.Tier == PurchaseTier.Construction && need.Price == 168_532);
    }

    [Fact]
    public async Task WhereEveryMarketIsShort_TheBuilderWaitsForTheSupply()
    {
        // D66: no purchase at SCARCE or LIMITED; what it waits for is still its place in the order.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler());
        PricesAre(Map(GateMarket(), F49Market(supply: "LIMITED"), D42Market(supply: "SCARCE"), H51Market(), I56Market()));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Waiting.Should().Be(ConstructionPlanner.LowSupply);
        _order.Of(AutomationPlan.Construction).Tier.Should().Be(PurchaseTier.Construction);
    }

    [Fact]
    public async Task AShipThatHoldsWhatTheGateNeeds_TakesItThere_WhateverItsRole()
    {
        // A trader left with 40 FAB_MATS, which no market here buys: it would have jettisoned them (D42).
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Trade));
        Fleet(Hauler(cargo: [new CargoItemModel("FAB_MATS", 40)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-6"].Should().BeOfType<SupplyConstructionGoal>().Subject;
        (trip.TradeSymbol, trip.Units, trip.CargoBought, trip.ReservedCredits).Should().Be(("FAB_MATS", 40, true, 0L));
        _log.Journal.Should().ContainSingle(entry => entry.EventKind == "ConstructionStarted" && Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task ASupplyTheGateRefusedLately_IsntOfferedAgainYet()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Trade));
        Fleet(Hauler(cargo: [new CargoItemModel("FAB_MATS", 40)]));
        _retries.Refused("SHIP-6", "FAB_MATS", DateTimeOffset.UtcNow);

        await RunAsync();

        _activeGoals.Should().BeEmpty();
    }

    [Fact]
    public async Task WhatOtherTripsCarry_IsNotBoughtAgain()
    {
        // 1,500 FAB_MATS are in and SHIP-7 carries 80: 20 are left, which the hauler takes in one purchase (D67).
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct), ("SHIP-7", FleetRole.Construct));
        _sites.NeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns([Site(fabMats: 1_500, circuitry: 400)]);
        _activeGoals["SHIP-7"] = new SupplyConstructionGoal { TradeSymbol = "FAB_MATS", ConstructionSiteWaypointSymbol = Gate, BuyWaypointSymbol = F49, Units = 80, CargoBought = true };
        Fleet(Hauler(), Shuttle() with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(5) });

        await RunAsync();

        ((SupplyConstructionGoal)_activeGoals["SHIP-6"]).Units.Should().Be(20);
    }

    [Fact]
    public async Task ABuilderThatHoldsOtherCargo_IsLeftToTheTradingPlan()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler(cargo: [new CargoItemModel("IRON", 10)]));

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _passedOver.MayTrade("SHIP-6", [AutomationPlan.Construction]).Should().BeTrue("the pass passed it over (B63)");
    }

    [Fact]
    public async Task WithTheRoleBoardOff_ItPicksTheLargestHoldItself()
    {
        // The survey plan is on: the command ship surveys (D20). Of the shuttle and the hauler, the hauler builds.
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(true);
        Fleet(CommandShip(), Shuttle(), Hauler());

        await RunAsync();

        _activeGoals.Keys.Should().Equal("SHIP-6");
    }

    [Fact]
    public async Task TheState_ListsTheGate_WhatIsOnItsWay_TheBuilders_AndWhoALoadWaitsFor()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        Fleet(Hauler());

        await RunAsync();

        var site = _state!.Sites.Should().ContainSingle().Subject;
        site.WaypointSymbol.Should().Be(Gate);
        site.Materials.Select(material => (material.TradeSymbol, material.Required, material.Fulfilled, material.OnTheWay))
            .Should().Equal(("FAB_MATS", 1_600, 0, 80), ("ADVANCED_CIRCUITRY", 400, 0, 0));
        _state.BuilderShipSymbols.Should().Equal("SHIP-6");
        _state.ReadyShipSymbols.Should().Equal("SHIP-6");
        _state.Waiting.Should().BeEmpty();
    }

    [Fact]
    public async Task WithNothingToBuild_ItTellsTheOrderItNeedsNothing()
    {
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        _sites.NeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ConstructionSiteModel>());
        Fleet(Hauler());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _order.Needs.Should().ContainKey(AutomationPlan.Construction).WhoseValue.Should().Be(PurchaseNeed.None);
    }

    [Fact]
    public async Task OnlyTheHomeSystemsGate_IsBuilt()
    {
        // D68: "Only the home base jump gate construction should be high priority, any other jump gate construction should be
        // low priority or maybe not even considered at all." With none of our ships at home, no gate is even looked at.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-6", FleetRole.Construct));
        _sites.HomeSystemAsync(Arg.Any<CancellationToken>()).Returns("X1-HZ59");
        Fleet(Hauler());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        await _sites.DidNotReceive().NeedingMaterialsAsync(Arg.Any<CancellationToken>());
        _order.Of(AutomationPlan.Construction).Should().Be(PurchaseNeed.None);
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void PricesAre(TradeMarketMap map) => _tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(new TradeContext(map, 1_000_000, 200, 5_000));

    private void Spendable(long credits)
        => _budget.EvaluateAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new BudgetDecision(true, credits + 100_000, 100_000, credits));

    private Task RunAsync()
        => new ConstructionPlanService(
                _ships,
                _goals,
                _assignments,
                _settings,
                _plans,
                _tradeContexts,
                _sites,
                _budget,
                _order,
                _retries,
                _passedOver,
                _log.For<ConstructionPlanService>())
            .EnsureBootstrappedAsync();
}

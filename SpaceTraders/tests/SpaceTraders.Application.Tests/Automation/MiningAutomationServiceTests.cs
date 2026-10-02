using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4: every free miner takes one trip at a time, a surveyed ore first, then an ore in low supply
/// (D22); ore it holds is sold first. A ship that can survey doesn't mine while the survey plan is on (D20),
/// and no drone is bought while the contract takes the miners (D23).
/// </summary>
public sealed class MiningAutomationServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly IShipPurchaseService _purchases = Substitute.For<IShipPurchaseService>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private MiningAutomationPlanState? _state;

    public MiningAutomationServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.MiningAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<MiningAutomationPlanState>(1));
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipyardWaypointDto
            {
                WaypointSymbol = H52,
                SystemSymbol = SystemSymbol,
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 48_328, FuelCapacity = 80, CargoCapacity = 15 }],
            },
        ]);
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = true });
    }

    [Fact]
    public async Task AFreeMiner_MinesTheOreInLowSupplyThatPaysMost_AndSellsItWhereItIsShort()
    {
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.TradeSymbol.Should().Be("COPPER_ORE");
        trip.SourceWaypointSymbol.Should().Be(XB5C);
        trip.SellWaypointSymbol.Should().Be(H51);
        trip.Selling.Should().BeFalse();

        var started = _log.Journal.Should().ContainSingle().Subject;
        started.EventKind.Should().Be("MiningStarted");
        started.Properties["Reason"].Should().Be("low_supply");
    }

    [Fact]
    public async Task AFreeMiner_MinesASurveyedOreFirst()
    {
        // A survey of XB5C that is mostly silicon: SILICON_CRYSTALS isn't the best paid, but it is surveyed.
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>())
            .Returns(Context(Survey("S-1", XB5C, "SILICON_CRYSTALS", "SILICON_CRYSTALS", "ICE_WATER")));
        Fleet(Drone());

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.TradeSymbol.Should().Be("SILICON_CRYSTALS");
        trip.SellWaypointSymbol.Should().Be(F49);
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "surveyed"));
    }

    [Fact]
    public async Task TwoMiners_NeverShareASellMarketAndOre()
    {
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"));

        await RunAsync();

        _activeGoals.Values.Cast<MineAndSellGoal>()
            .Select(trip => (trip.SellWaypointSymbol, trip.TradeSymbol))
            .Should().BeEquivalentTo([(H51, "COPPER_ORE"), (H51, "IRON_ORE")]);
    }

    [Fact]
    public async Task AMinerHoldingOre_SellsItFirst()
    {
        // Ore left over from the contract (D23).
        Fleet(Drone(waypoint: XB5C, status: "IN_ORBIT", cargo: [new CargoItemModel("COPPER_ORE", 9)]));

        await RunAsync();

        var trip = _activeGoals["SHIP-3"].Should().BeOfType<MineAndSellGoal>().Subject;
        trip.Selling.Should().BeTrue();
        trip.TradeSymbol.Should().Be("COPPER_ORE");
        trip.SellWaypointSymbol.Should().Be(H51);
        _log.Journal.Should().ContainSingle(entry => Equals(entry.Properties["Reason"], "held_cargo"));
    }

    [Fact]
    public async Task AShipThatCanSurvey_DoesNotMine_WhileTheSurveyPlanIsOn()
    {
        Fleet(CommandShip());
        SurveyPlanIs(on: true);

        await RunAsync();
        _activeGoals.Should().BeEmpty();

        SurveyPlanIs(on: false);

        await RunAsync();
        _activeGoals["SHIP-1"].Should().BeOfType<MineAndSellGoal>();
    }

    [Fact]
    public async Task AMinerWithAnotherGoal_OrAnAssignment_OrInTransit_IsNotFree()
    {
        _activeGoals["SHIP-3"] = new TradeBetweenMarketsGoal { TradeSymbol = "FOOD", BuyWaypointSymbol = H51, SellWaypointSymbol = F49 };
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(
            [new ShipAssignmentDto("SHIP-4", "Contract", XB5C, H51, "COPPER_ORE", "C-1", 0, Now, null)]);
        Fleet(Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<TradeBetweenMarketsGoal>();
    }

    [Fact]
    public async Task WithNoMinerFree_ItBuysADroneForEachOpeningADroneCanReach()
    {
        // Every miner works; four openings near the middle wait, and B7's two are beyond a drone's tank.
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        Fleet(Drone());

        await RunAsync();

        await _purchases.Received(3).TryPurchaseAsync("SHIP_MINING_DRONE", H52, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoDroneIsBought_WhileTheContractTakesTheMiners()
    {
        // D23: every free miner joins the contract, a new drone too; the contract plan buys at most one.
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Contract), Arg.Any<CancellationToken>()).Returns(true);
        _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-4",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = XB5C,
            DestinationWaypoint = H51,
            UnitsRequired = 145,
            UnitsFulfilled = 45,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        Fleet(Drone());

        await RunAsync();

        await _purchases.DidNotReceiveWithAnyArgs().TryPurchaseAsync(default!, default!, default);
    }

    [Fact]
    public async Task TheState_ListsTheOpenings_WithTheMinersThatCouldTakeThem()
    {
        // The drone takes copper; iron, silicon and quartz stay open, and so do B7's, which it can't reach.
        Fleet(Drone(), Drone("SHIP-4") with { Status = "IN_TRANSIT", ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(1) });

        await RunAsync();

        _state!.Opportunities.Should().ContainSingle(o => o.Status == MarketAutomationOpportunityStatus.Assigned)
            .Which.AssignedShipSymbol.Should().Be("SHIP-3");
        _state.Opportunities.Where(o => o.Status == MarketAutomationOpportunityStatus.Pending).Should().HaveCount(5)
            .And.OnlyContain(o => o.CandidateShipSymbols.Count == 0, "the only free miner took a trip");
    }

    [Fact]
    public async Task TheState_IsWrittenOnlyWhenItChanges()
    {
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        _purchases.TryPurchaseAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ShipPurchaseResult { IsSuccess = false, FailureReason = "budget" });
        Fleet(Drone());

        await RunAsync();
        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.MiningAutomation, Arg.Any<MiningAutomationPlanState>(), Arg.Any<CancellationToken>());
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void SurveyPlanIs(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Survey), Arg.Any<CancellationToken>()).Returns(on);

    private Task RunAsync()
        => new MiningAutomationService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _shipyards,
                _contexts,
                _settings,
                _plans,
                _purchases,
                _log.For<MiningAutomationService>())
            .EnsureBootstrappedAsync();
}

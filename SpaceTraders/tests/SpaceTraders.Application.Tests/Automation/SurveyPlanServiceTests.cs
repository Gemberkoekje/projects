using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Tests.Roles;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4: every ship that can survey surveys (D20): the contract's ore at the contract's asteroid
/// first, otherwise ores the markets buy, at the asteroid nearest their buyer that the miners can reach;
/// each until it has a stock of usable surveys, and then nothing (D27).
/// </summary>
public sealed class SurveyPlanServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISurveyKeeper _surveyKeeper = Substitute.For<ISurveyKeeper>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ShipGoalStepGuard _stepGuard = new();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private SurveyPlanState? _state;

    public SurveyPlanServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _goals.When(goals => goals.SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>()))
            .Do(call => _activeGoals[call.ArgAt<string>(0)] = call.ArgAt<ShipGoal>(1));
        _plans.GetAsync<SurveyPlanState>(PlanTypes.Survey, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.Survey, Arg.Any<SurveyPlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<SurveyPlanState>(1));
    }

    [Fact]
    public async Task TheCommandShip_SurveysTheContractsOre_AtTheContractsAsteroid()
    {
        ContractMines("COPPER_ORE", XB5C, H51);
        Fleet(CommandShip(), Drone());

        await RunAsync();

        var survey = _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        survey.TargetWaypointSymbol.Should().Be(XB5C);
        survey.TargetDepositSymbol.Should().Be("COPPER_ORE");
    }

    [Fact]
    public async Task WithoutAContract_ItSurveysTheBestPaidOre_ThatTheMinersCanReach()
    {
        // GOLD pays most, at B7, but the drone can't reach B14: COPPER at XB5C, for H51, it is.
        Fleet(CommandShip(), Drone());

        await RunAsync();

        var survey = _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Subject;
        survey.TargetWaypointSymbol.Should().Be(XB5C);
        survey.TargetDepositSymbol.Should().Be("COPPER_ORE");
    }

    [Fact]
    public async Task WithTheRoleBoardOn_OnlyTheShipWithTheSurveyRole_Surveys()
    {
        // Slice 6.9 (D38): a ship that can only survey surveys, so the command ship, with the trade role, doesn't.
        RoleBoardTestSupport.RolesAre(_settings, _plans, ("SHIP-1", FleetRole.Trade), ("SHIP-5", FleetRole.Survey));
        Fleet(CommandShip(), Drone(), SurveyShip());

        await RunAsync();

        _activeGoals.Keys.Should().Equal("SHIP-5");
        _activeGoals["SHIP-5"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
    }

    [Fact]
    public async Task ASecondSurveyor_TakesTheNextTarget()
    {
        _activeGoals["SHIP-1"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        Fleet(CommandShip(), CommandShip(symbol: "SHIP-2"), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("COPPER_ORE");
        _activeGoals["SHIP-2"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task ShipsThatCantSurvey_AndBusySurveyors_GetNoSurvey()
    {
        _activeGoals["SHIP-1"] = new ScoutWaypointGoal { TargetWaypointSymbol = H51 };
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals.Should().ContainSingle().Which.Value.Should().BeOfType<ScoutWaypointGoal>();
    }

    [Fact]
    public async Task OnceTheContractsOreHasItsStock_TheCommandShipSurveysForTheNextOre()
    {
        // D27 (2026-10-02): "I'd expect him to make 1 copper ore survey and then move to the next ore
        // type". The stock is two usable surveys per ore, unless the setting says otherwise.
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(Survey("S-1", XB5C, "COPPER_ORE"), Survey("S-2", XB5C, "COPPER_ORE"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task TheStock_IsASetting()
    {
        _settings.GetAsync<int>(SurveyPlanService.StockPerOreSetting, Arg.Any<CancellationToken>()).Returns(1);
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(Survey("S-1", XB5C, "COPPER_ORE"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetDepositSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public async Task WithEveryOreStocked_TheSurveyorWaits()
    {
        ContractMines("COPPER_ORE", XB5C, H51);
        SurveysAre(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals.Should().BeEmpty();
        _state!.Targets.Should().HaveCount(6).And.OnlyContain(target => !target.NeedsSurvey && target.UsableSurveys == 2);
    }

    [Fact]
    public async Task EachPass_EndsTheSurveysThatExpired()
    {
        Fleet(Drone());

        await RunAsync();

        await _surveyKeeper.Received(1).ExpireAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheState_ListsTheTargets_AndIsWrittenOnlyWhenItChanges()
    {
        ContractMines("COPPER_ORE", XB5C, H51);
        _activeGoals["SHIP-1"] = new SurveyWaypointGoal { TargetWaypointSymbol = XB5C, TargetDepositSymbol = "COPPER_ORE" };
        Fleet(CommandShip(), Drone());

        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.Survey, Arg.Any<SurveyPlanState>(), Arg.Any<CancellationToken>());
        var first = _state!.Targets[0];
        first.ForContract.Should().BeTrue();
        first.SurveyorShipSymbols.Should().Equal("SHIP-1");
        _state.Targets.Should().HaveCount(6, "the contract's copper, then five sellable ores at XB5C");
    }

    [Fact]
    public async Task ASurveyThatNeedsTaking_TakesTheCommandShipOffItsSpareTimeTrip_WithItsHoldAboard()
    {
        // D37 (2026-10-02): "It surveys on the spot with the hold aboard, then carries on filling, and sells once full."
        var gathering = CommandShip() with { CargoCurrent = 12, CargoInventory = [new CargoItemModel("COPPER_ORE", 12)] };
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(gathering);
        Fleet(gathering, Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>().Which.TargetWaypointSymbol.Should().Be(XB5C);
        var interrupted = _log.Journal.Should().ContainSingle().Subject;
        interrupted.EventKind.Should().Be("GatheringInterrupted");
        interrupted.Properties["Reason"].Should().Be("survey");
        interrupted.Properties["Units"].Should().Be(12);
        interrupted.Properties["WaypointSymbol"].Should().Be(XB5C);
    }

    [Fact]
    public async Task WithEveryOreStocked_TheCommandShipGathersOn()
    {
        SurveysAre(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip());
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task ASpareTimeTripThatSells_IsNotInterrupted()
    {
        // It is nearly done; the plans choose again after its sales.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C, Selling = true };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip(H51, "DOCKED"));
        Fleet(CommandShip(H51, "DOCKED"), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
    }

    [Fact]
    public async Task AShipWhoseArrivalIsNotRecordedYet_IsNotTakenOffItsTrip()
    {
        // B17: the arrival matches the trip's goal. The fleet as the plans read it reckons the ship arrived; as
        // stored, it is still in flight until the arrival is recorded.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip(H51, "IN_TRANSIT") with { DestWaypointSymbol = XB5C, ArrivesAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);
        _log.Journal.Should().BeEmpty();
    }

    [Fact]
    public async Task AShipWhoseGoalStepRuns_IsTakenOffItsTripOnALaterTick()
    {
        // B46: a step that turns the trip to selling would write it back over the survey.
        var trip = new GatherAndSellGoal { SourceWaypointSymbol = XB5C };
        _activeGoals["SHIP-1"] = trip;
        Stored(CommandShip());
        Fleet(CommandShip(), Drone());
        _stepGuard.TryEnter("SHIP-1").Should().BeTrue();

        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeSameAs(trip);

        _stepGuard.Exit("SHIP-1");
        await RunAsync();

        _activeGoals["SHIP-1"].Should().BeOfType<SurveyWaypointGoal>();
    }

    private void Stored(ShipModel ship) => _ships.FindAsync(ship.Symbol, Arg.Any<CancellationToken>()).Returns(ship);

    private void ContractMines(string ore, string asteroid, string destination)
        => _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-3",
            TradeSymbol = ore,
            SourceWaypoint = asteroid,
            DestinationWaypoint = destination,
            UnitsRequired = 145,
            UnitsFulfilled = 45,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>A survey ship, as bought: a surveyor, and nothing to carry anything in.</summary>
    private static ShipModel SurveyShip()
        => new("SHIP-5", SystemSymbol, XB5C, "IN_ORBIT", "CRUISE", 80, 80, ShipType: "SHIP_SURVEYOR", MountSymbols: ["MOUNT_SURVEYOR_I"]);

    private void SurveysAre(params SurveyModel[] surveys)
        => _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context(surveys));

    private Task RunAsync()
        => new SurveyPlanService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _contexts,
                _surveyKeeper,
                _plans,
                _settings,
                new SpareTimeInterruption(_ships, _goals, _stepGuard, _log.For<SpareTimeInterruption>()),
                _log.For<SurveyPlanService>())
            .EnsureBootstrappedAsync();
}

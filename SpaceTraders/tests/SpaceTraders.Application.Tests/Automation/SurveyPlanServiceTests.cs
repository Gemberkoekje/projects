using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.4: every ship that can survey surveys (D20): the contract's ore at the contract's asteroid
/// first, otherwise ores the markets buy, at the asteroid nearest their buyer that the miners can reach.
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

    private Task RunAsync()
        => new SurveyPlanService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _contexts,
                _surveyKeeper,
                _plans,
                _log.For<SurveyPlanService>())
            .EnsureBootstrappedAsync();
}

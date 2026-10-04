using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Automation;

/// <summary>
/// Slice 6.9 (D38, D41): the role board weighs every ship's role at a start, every <c>Roles.ReconsiderMinutes</c>, and at
/// once when a ship joins, a plan is switched, the contract starts or stops wanting ore, or a ship with a choice of roles
/// has had no work for a minute. It writes each ship's role and estimates, and journals each change.
/// </summary>
public sealed class RolePlanServiceTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IContractMineralPlanRepository _contractPlans = Substitute.For<IContractMineralPlanRepository>();
    private readonly IMiningContextReader _contexts = Substitute.For<IMiningContextReader>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly GatheringRates _rates = new();
    private readonly RoleBoardMemory _memory = new();
    private readonly IConstructionSites _constructionSites = Substitute.For<IConstructionSites>();
    private readonly LogRecorder _log = new();
    private readonly Dictionary<string, ShipGoal> _activeGoals = new(StringComparer.OrdinalIgnoreCase);
    private RolePlanState? _state;

    public RolePlanServiceTests()
    {
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipAssignmentDto>());
        _contexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(_ => Context());
        _goals.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _activeGoals.GetValueOrDefault(call.Arg<string>()));
        _plans.GetAsync<RolePlanState>(PlanTypes.Roles, Arg.Any<CancellationToken>()).Returns(_ => _state);
        _plans.When(plans => plans.UpsertAsync(PlanTypes.Roles, Arg.Any<RolePlanState>(), Arg.Any<CancellationToken>()))
            .Do(call => _state = call.ArgAt<RolePlanState>(1));
        _settings.GetAsync<int>("Trade.MinProfitPerUnit", Arg.Any<CancellationToken>()).Returns(200);
        _constructionSites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ConstructionSiteModel>());
        On(AutomationPlan.Survey, AutomationPlan.Mining, AutomationPlan.Trading);
    }

    [Fact]
    public async Task TheFirstPass_GivesEveryShipItsRole_JournalsIt_AndWritesTheBoard()
    {
        // The command ship is the only ship that can survey, and a drone can mine: it surveys (D38). Silicon, quartz,
        // copper and iron are SCARCE or LIMITED near the middle of X1-DC53, and the drone is the only one to mine them: it
        // keeps mining (D48).
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _state!.Ships.Select(ship => (ship.ShipSymbol, ship.Role, ship.Reason)).Should().Equal(
            ("SHIP-1", FleetRole.Survey, RolePlanner.SurveyFirst),
            ("SHIP-3", FleetRole.Mine, RolePlanner.Coverage));
        _state.Ships[1].Estimates.Select(estimate => estimate.Role).Should().Equal(FleetRole.Mine, FleetRole.Trade);
        _state.Ships[1].Rates.Should().ContainSingle().Which.Should().Be(new RoleRateState { Kind = GatheringKind.Mining, UnitsPerAction = 3, SecondsPerAction = 70, Observed = false });

        _log.Journal.Select(entry => (entry.EventKind, entry.Properties["ShipSymbol"], entry.Properties["NewRole"])).Should().Equal(
            ("RoleChanged", "SHIP-1", (object)FleetRole.Survey),
            ("RoleChanged", "SHIP-3", (object)FleetRole.Mine));
    }

    [Fact]
    public async Task OneDronePerScarceOreAndArea_KeepsMining_AndTheRestShareTheWork()
    {
        // Slice 6.10b (D48): six ores and areas are SCARCE or LIMITED for a drone, four near the middle, and B7's gold and
        // copper, a drift away (D45); copper counts in each area (D53). There are seven drones. The seventh mines too: a drone
        // gathers first (D58).
        Fleet(CommandShip(), Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-8"), Drone("SHIP-9"));

        await RunAsync();

        _state!.Ships.Where(ship => ship.Reason == RolePlanner.Coverage).Select(ship => ship.ShipSymbol)
            .Should().Equal("SHIP-3", "SHIP-4", "SHIP-5", "SHIP-6", "SHIP-7", "SHIP-8");
        _state.Ships.Single(ship => ship.ShipSymbol == "SHIP-9").Should().Match<RoleShipState>(
            ship => ship.Role == FleetRole.Mine && ship.Reason == RolePlanner.GathersFirst);
    }

    [Fact]
    public async Task ADroneWorkingOnAScarceOre_IsTheOneKeptForIt()
    {
        HeldBy("SHIP-9", H51, "COPPER_ORE");
        Fleet(CommandShip(), Drone("SHIP-3"), Drone("SHIP-4"), Drone("SHIP-5"), Drone("SHIP-6"), Drone("SHIP-7"), Drone("SHIP-8"), Drone("SHIP-9"));

        await RunAsync();

        _state!.Ships.Where(ship => ship.Reason == RolePlanner.Coverage).Select(ship => ship.ShipSymbol)
            .Should().BeEquivalentTo(["SHIP-9", "SHIP-3", "SHIP-4", "SHIP-5", "SHIP-6", "SHIP-7"]);
    }

    [Fact]
    public async Task WithinTheInterval_NothingIsWeighedAgain()
    {
        Fleet(CommandShip(), Drone());

        await RunAsync();
        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.Roles, Arg.Any<RolePlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnceTheIntervalHasPassed_TheRolesAreWeighedAgain_AndARoleThatStaysKeepsItsSince()
    {
        Fleet(CommandShip(), Drone());
        await RunAsync();
        var since = _state!.Ships[1].Since;
        _memory.Evaluated(DateTimeOffset.UtcNow.AddMinutes(-RoleSettings.DefaultReconsiderMinutes - 1));

        await RunAsync();

        await _plans.Received(2).UpsertAsync(PlanTypes.Roles, Arg.Any<RolePlanState>(), Arg.Any<CancellationToken>());
        _state.Ships[1].Since.Should().Be(since);
        _log.Journal.Should().HaveCount(2, "no role changed the second time");
    }

    [Fact]
    public async Task ANewShip_IsGivenItsRoleAtOnce()
    {
        Fleet(CommandShip(), Drone());
        await RunAsync();
        Fleet(CommandShip(), Drone(), Drone("SHIP-4"));

        await RunAsync();

        _state!.Ships.Select(ship => ship.ShipSymbol).Should().Contain("SHIP-4");
    }

    [Fact]
    public async Task APlanSwitchedOff_WeighsTheRolesAgainAtOnce()
    {
        // With the survey plan off, nobody surveys: the command ship takes what pays it most.
        Fleet(CommandShip(), Drone());
        await RunAsync();
        On(AutomationPlan.Mining, AutomationPlan.Trading);

        await RunAsync();

        _state!.Ships[0].Role.Should().Be(FleetRole.Mine);
        _log.Journal.Last().Properties["OldRole"].Should().Be(FleetRole.Survey);
    }

    [Fact]
    public async Task AShipWithAChoiceOfRoles_LeftWithoutWorkForAMinute_HasItsRoleWeighedAgain()
    {
        Fleet(CommandShip(), Drone());
        await RunAsync();
        var now = DateTimeOffset.UtcNow;
        _memory.Evaluated(now.AddMinutes(-2));
        _memory.FreeFor("SHIP-3", free: false, now.AddMinutes(-3));
        _memory.FreeFor("SHIP-3", free: true, now.AddMinutes(-2));

        await RunAsync();

        await _plans.Received(2).UpsertAsync(PlanTypes.Roles, Arg.Any<RolePlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheShipThatSurveys_WaitingForSurveys_DoesntHaveTheRolesWeighedAgain()
    {
        // The drone is busy; the command ship surveys, and waits between surveys by design.
        _activeGoals["SHIP-3"] = new MineAndSellGoal { TradeSymbol = "COPPER_ORE", SourceWaypointSymbol = XB5C, SellWaypointSymbol = H51 };
        Fleet(CommandShip(), Drone());
        await RunAsync();
        var now = DateTimeOffset.UtcNow;
        _memory.Evaluated(now.AddMinutes(-2));
        _memory.FreeFor("SHIP-1", free: false, now.AddMinutes(-3));
        _memory.FreeFor("SHIP-1", free: true, now.AddMinutes(-2));

        await RunAsync();

        await _plans.Received(1).UpsertAsync(PlanTypes.Roles, Arg.Any<RolePlanState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterARestart_TheRolesAreWeighedAgain_WithTheRatesTheBoardKept()
    {
        _state = new RolePlanState
        {
            EvaluatedAt = DateTimeOffset.UtcNow,
            Conditions = "Survey,Mining,Trading",
            Ships =
            [
                new RoleShipState
                {
                    ShipSymbol = "SHIP-3",
                    Role = FleetRole.Mine,
                    Reason = RolePlanner.MostProfitable,
                    Since = DateTimeOffset.UtcNow.AddHours(-1),
                    Rates = [new RoleRateState { Kind = GatheringKind.Mining, UnitsPerAction = 6, SecondsPerAction = 80, Observed = true }],
                },
            ],
        };
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _rates.For("SHIP-3", GatheringKind.Mining).Should().Be(new GatheringRate(6, 80, Observed: true));
        _state.Ships.Single(ship => ship.ShipSymbol == "SHIP-3").Rates.Single().UnitsPerAction.Should().Be(6);
    }

    [Fact]
    public async Task WhileTheContractWantsOre_TheDroneMinesForIt_AndTheBoardSaysSo()
    {
        On(AutomationPlan.Contract, AutomationPlan.Survey, AutomationPlan.Mining, AutomationPlan.Trading);
        _contractPlans.GetAsync(Arg.Any<CancellationToken>()).Returns(new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = "C-1",
            ShipSymbol = "SHIP-3",
            TradeSymbol = "COPPER_ORE",
            SourceWaypoint = XB5C,
            DestinationWaypoint = H51,
            UnitsRequired = 100,
            UnitsFulfilled = 10,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        Fleet(CommandShip(), Drone());

        await RunAsync();

        _state!.Ships.Single(ship => ship.ShipSymbol == "SHIP-3").Reason.Should().Be(RolePlanner.Contract);
        _state.Conditions.Should().EndWith("|contract wants ore");
    }

    [Fact]
    public async Task WhileTheHomeGateNeedsMaterials_TheLargestHoldBuildsIt_AndItsCompletionFreesItAtOnce()
    {
        // Slice 6.6 (D65, D68): a light hauler, the only cargo ship, builds the home system's jump gate; the command ship surveys.
        On(AutomationPlan.Survey, AutomationPlan.Mining, AutomationPlan.Trading, AutomationPlan.Construction);
        _constructionSites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns([Construction.ConstructionFixture.Site()]);
        var hauler = new ShipModel("SHIP-6", SystemSymbol, H51, "DOCKED", "CRUISE", 600, 600, CargoCapacity: 80, ShipType: "SHIP_LIGHT_HAULER", MountSymbols: ["MOUNT_TURRET_I"], CargoInventory: []);
        Fleet(CommandShip(), Drone(), hauler);

        await RunAsync();

        _state!.Ships.Single(ship => ship.ShipSymbol == "SHIP-6").Should().Match<RoleShipState>(ship => ship.Role == FleetRole.Construct && ship.Reason == RolePlanner.Construction);
        _state.Ships.Single(ship => ship.ShipSymbol == "SHIP-6").Estimates.Select(estimate => estimate.Role).Should().Equal(FleetRole.Trade);
        _log.Journal.Should().Contain(entry => entry.EventKind == "RoleChanged" && Equals(entry.Properties["ShipSymbol"], "SHIP-6") && Equals(entry.Properties["NewRole"], FleetRole.Construct));

        // The gate is complete: the roles are weighed again at once, and the hauler trades.
        _constructionSites.CachedNeedingMaterialsAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ConstructionSiteModel>());

        await RunAsync();

        _state.Ships.Single(ship => ship.ShipSymbol == "SHIP-6").Role.Should().Be(FleetRole.Trade);
    }

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    private void HeldBy(string ship, string market, string ore)
        => _activeGoals[ship] = new MineAndSellGoal { TradeSymbol = ore, SourceWaypointSymbol = XB5C, SellWaypointSymbol = market };

    private void On(params AutomationPlan[] plans)
    {
        foreach (var plan in Enum.GetValues<AutomationPlan>())
        {
            _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(plan), Arg.Any<CancellationToken>()).Returns(plans.Contains(plan));
        }
    }

    private Task RunAsync()
        => new RolePlanService(
                _ships,
                _goals,
                _assignments,
                _contractPlans,
                _contexts,
                _settings,
                _plans,
                _rates,
                _memory,
                _constructionSites,
                _log.For<RolePlanService>())
            .EnsureBootstrappedAsync();
}

#pragma warning disable IDISP001, IDISP004

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class GameLoopServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceScopeFactory _serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IScoutAllMarketplacesPlanService _scoutPlan = Substitute.For<IScoutAllMarketplacesPlanService>();
    private readonly IContractPlanService _contractPlan = Substitute.For<IContractPlanService>();
    private readonly IProbeDeploymentPlanService _probePlan = Substitute.For<IProbeDeploymentPlanService>();
    private readonly ISurveyPlanService _surveyPlan = Substitute.For<ISurveyPlanService>();
    private readonly IMiningAutomationService _miningPlan = Substitute.For<IMiningAutomationService>();
    private readonly ISiphonAutomationService _siphonPlan = Substitute.For<ISiphonAutomationService>();
    private readonly ITradingAutomationService _tradingPlan = Substitute.For<ITradingAutomationService>();
    private readonly ISpareTimePlanService _spareTimePlan = Substitute.For<ISpareTimePlanService>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();
    private readonly IMarketWatchService _marketWatch = Substitute.For<IMarketWatchService>();
    private readonly IApiAvailabilityState _apiAvailability = Substitute.For<IApiAvailabilityState>();

    public GameLoopServiceTests()
    {
        _serviceProvider = new ServiceCollection()
            .AddSingleton(_bus)
            .AddSingleton(_settings)
            .AddSingleton(_scoutPlan)
            .AddSingleton(_contractPlan)
            .AddSingleton(_probePlan)
            .AddSingleton(_surveyPlan)
            .AddSingleton(_miningPlan)
            .AddSingleton(_siphonPlan)
            .AddSingleton(_tradingPlan)
            .AddSingleton(_spareTimePlan)
            .AddSingleton(_assignments)
            .AddSingleton(_ships)
            .AddSingleton(_goalExecutor)
            .AddSingleton(_marketWatch)
            .BuildServiceProvider();

        _serviceScopeFactory.CreateScope().Returns(_ => _serviceProvider.CreateScope());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SPECTER-DEBUG-3", "X1-PT96", "X1-PT96-H53", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15),
            new ShipModel("SPECTER-DEBUG-5", "X1-PT96", "X1-PT96-D42", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40),
        ]);
        _ships.FindAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SPECTER-DEBUG-5", "X1-PT96", "X1-PT96-D42", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([ContractAssignment("SPECTER-DEBUG-5")]);
    }

    public void Dispose() => _serviceProvider.Dispose();

    [Fact]
    public async Task Tick_PutsTheTickAndThePlanOrShipOfEachStepInTheLogContext()
    {
        // B12: nothing told which tick, plan or ship a line logged during a tick belonged to.
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");
        var logger = Substitute.For<ILogger<GameLoopService>>();

        await TickAsync(logger);

        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(context => context.ContainsKey("Tick")));
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(context => Has(context, "Plan", AutomationPlan.Scout)));
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(context => Has(context, "ShipSymbol", "SPECTER-DEBUG-3")));
        logger.Received(1).BeginScope(Arg.Is<Dictionary<string, object>>(context => Has(context, "ContractId", "CONTRACT-1") && Has(context, "ShipSymbol", "SPECTER-DEBUG-5")));
    }

    [Fact]
    public async Task Tick_ExecutesGoalExecutor_ForAllShipsNotJustScouts()
    {
        SwitchOn("Automation.Enabled");

        await TickAsync();

        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>());
        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_WithAutomationDisabled_IssuesNoShipCommands()
    {
        SwitchOn("Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled", "Automation.Plan.ProbeDeployment.Enabled", "Automation.Plan.Mining.Enabled", "Automation.Plan.Siphon.Enabled", "Automation.Plan.Trading.Enabled", "Automation.Plan.SpareTime.Enabled");

        await TickAsync();

        await _scoutPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _contractPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _probePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _miningPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _siphonPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _tradingPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _spareTimePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _marketWatch.DidNotReceive().RefreshDueMarketAsync(Arg.Any<CancellationToken>());
        _bus.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_RefreshesAMarketLast_AfterThePlansTheShipsAndTheContractWork()
    {
        // D19: a refresh is a read, which can go later without loss; a move or a trade can't.
        SwitchOn("Automation.Enabled", "Automation.Plan.Trading.Enabled", "Automation.Plan.Contract.Enabled");
        var steps = new List<string>();
        _tradingPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "plan"));
        _goalExecutor.ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => RecordShipStep(steps));
        _bus.InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "contract"));
        _marketWatch.RefreshDueMarketAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "market"));

        await TickAsync();

        steps.Should().Equal("plan", "ship", "ship", "contract", "market");
    }

    [Fact]
    public async Task Tick_AThrowingMarketWatch_IsLoggedAndTheNextTickRuns()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Trading.Enabled");
        _marketWatch.RefreshDueMarketAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("API error.")));

        await TickAsync();
        await TickAsync();

        await _marketWatch.Received(2).RefreshDueMarketAsync(Arg.Any<CancellationToken>());
        await _tradingPlan.Received(2).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_BootstrapsTheSurveyPlan_BeforeTheMiningAndTradingPlans()
    {
        // Slice 6.4: the survey plan claims the ships that can survey first (D20).
        SwitchOn("Automation.Enabled", "Automation.Plan.Survey.Enabled", "Automation.Plan.Mining.Enabled", "Automation.Plan.Trading.Enabled");
        var steps = new List<string>();
        _surveyPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "survey"));
        _miningPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "mining"));
        _tradingPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "trading"));

        await TickAsync();

        steps.Should().Equal("survey", "mining", "trading");
    }

    [Fact]
    public async Task Tick_BootstrapsTheSiphonPlan_AfterTheMiningPlan_AndBeforeTheTradingPlan()
    {
        // Slice 6.7: as with the miners, a siphoner trades only when the siphon plan has no trip for it.
        SwitchOn("Automation.Enabled", "Automation.Plan.Mining.Enabled", "Automation.Plan.Siphon.Enabled", "Automation.Plan.Trading.Enabled");
        var steps = new List<string>();
        _miningPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "mining"));
        _siphonPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "siphon"));
        _tradingPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "trading"));

        await TickAsync();

        steps.Should().Equal("mining", "siphon", "trading");
    }

    [Fact]
    public async Task Tick_BootstrapsTheSpareTimePlan_Last()
    {
        // Slice 6.8 (D34): the command ship mines or siphons only when it has nothing to survey and no trade.
        SwitchOn("Automation.Enabled", "Automation.Plan.Survey.Enabled", "Automation.Plan.Trading.Enabled", "Automation.Plan.SpareTime.Enabled");
        var steps = new List<string>();
        _surveyPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "survey"));
        _tradingPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "trading"));
        _spareTimePlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>()).Returns(_ => Record(steps, "spare time"));

        await TickAsync();

        steps.Should().Equal("survey", "trading", "spare time");
    }

    [Fact]
    public async Task Tick_BootstrapsOnlyThePlansThatAreSwitchedOn()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");

        await TickAsync();

        await _scoutPlan.Received(1).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _contractPlan.Received(1).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _probePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _surveyPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _miningPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _siphonPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _tradingPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _spareTimePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_WithTheContractPlanSwitchedOff_RunsNoContractCommands()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled");

        await TickAsync();

        await _contractPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<FulfillContractDeliveryCommand>(), Arg.Any<CancellationToken>());
        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_AThrowingPlan_DoesNotStopTheOtherPlansOrTheShips()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");
        _scoutPlan.EnsureBootstrappedAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("No ship with fuel.")));

        await TickAsync();

        await _contractPlan.Received(1).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>());
        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_AThrowingShipStep_DoesNotStopTheOtherShipsOrTheContractWork()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");
        _goalExecutor.ExecuteAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<GoalExecutionResult?>(new InvalidOperationException("API error.")));

        await TickAsync();

        await _goalExecutor.Received().ExecuteAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_AThrowingContractCommand_DoesNotStopTheNextAssignment()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Contract.Enabled");
        _ships.FindAsync("SPECTER-DEBUG-3", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SPECTER-DEBUG-3", "X1-PT96", "X1-PT96-H53", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            ContractAssignment("SPECTER-DEBUG-3"),
            ContractAssignment("SPECTER-DEBUG-5"),
        ]);
        _bus.InvokeAsync(Arg.Is<MineResourceVolumeCommand>(c => c.ShipSymbol == "SPECTER-DEBUG-3"), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("API error.")));

        await TickAsync();

        await _bus.Received(1).InvokeAsync(Arg.Is<MineResourceVolumeCommand>(c => c.ShipSymbol == "SPECTER-DEBUG-5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tick_WhileApiCallsArePausedAfterA502_SkipsTheWork()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");
        _apiAvailability.PausedUntil.Returns(TimeProvider.System.GetUtcNow().AddMinutes(2));

        await TickAsync();

        await _scoutPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _contractPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
        await _marketWatch.DidNotReceive().RefreshDueMarketAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(40, 1, 0, false)]
    [InlineData(40, 39, 0, false)]
    [InlineData(40, 40, 0, true)]
    [InlineData(60, 40, 0, true)]
    [InlineData(3, 3, 0, true)]
    [InlineData(40, 0, 40, false)]
    [InlineData(0, 0, 0, true)]
    public async Task Tick_SendsAContractShipToDeliverOnlyWithAWholeTrip(int requiredUnits, int contractUnits, int otherUnits, bool delivers)
    {
        // B8: the tick sent the ship to deliver as soon as one unit was aboard, so it shuttled one to
        // three units a trip. A trip carries what the contract still needs, at most a full hold (40
        // here); a hold full of other goods keeps mining, which jettisons them. With nothing left to
        // deliver, the delivery command fulfils the contract.
        SwitchOn("Automation.Enabled", "Automation.Plan.Contract.Enabled");
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([ContractAssignment("SPECTER-DEBUG-5", requiredUnits)]);
        List<CargoItemModel> cargo = [new("IRON_ORE", contractUnits), new("COPPER_ORE", otherUnits)];
        _ships.FindAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>())
            .Returns(new ShipModel(
                "SPECTER-DEBUG-5",
                "X1-PT96",
                "X1-PT96-AST",
                "IN_ORBIT",
                "CRUISE",
                80,
                80,
                CargoCurrent: contractUnits + otherUnits,
                CargoCapacity: 40,
                CargoInventory: cargo.Where(item => item.Units > 0).ToList()));

        await TickAsync();

        await _bus.Received(delivers ? 1 : 0).InvokeAsync(Arg.Any<FulfillContractDeliveryCommand>(), Arg.Any<CancellationToken>());
        await _bus.Received(delivers ? 0 : 1).InvokeAsync(Arg.Any<MineResourceVolumeCommand>(), Arg.Any<CancellationToken>());
    }

    private static ShipAssignmentDto ContractAssignment(string shipSymbol, int requiredUnits = 40) =>
        new(
            ShipSymbol: shipSymbol,
            AssignmentType: "Contract",
            OriginWaypoint: "X1-PT96-AST",
            DestWaypoint: "X1-PT96-D42",
            CargoSymbol: "IRON_ORE",
            ContractId: "CONTRACT-1",
            StepIndex: 0,
            AssignedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            RequiredUnits: requiredUnits);

    private void SwitchOn(params string[] keys)
    {
        foreach (var key in keys)
        {
            _settings.GetAsync<bool>(key, Arg.Any<CancellationToken>()).Returns(true);
        }
    }

    private static Task Record(List<string> steps, string step)
    {
        steps.Add(step);
        return Task.CompletedTask;
    }

    private static Task<GoalExecutionResult?> RecordShipStep(List<string> steps)
    {
        steps.Add("ship");
        return Task.FromResult<GoalExecutionResult?>(null);
    }

    private static bool Has(Dictionary<string, object> context, string key, object value)
        => context.TryGetValue(key, out var actual) && Equals(actual, value);

    private async Task TickAsync(ILogger<GameLoopService>? logger = null)
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        using var sut = new GameLoopService(
            _serviceScopeFactory,
            _apiAvailability,
            leaderElection,
            logger ?? NullLogger<GameLoopService>.Instance);

        var tickMethod = typeof(GameLoopService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)tickMethod.Invoke(sut, [CancellationToken.None])!;
    }
}

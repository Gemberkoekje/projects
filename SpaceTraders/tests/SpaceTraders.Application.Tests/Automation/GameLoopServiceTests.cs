#pragma warning disable IDISP001, IDISP004

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IMiningAutomationService _miningPlan = Substitute.For<IMiningAutomationService>();
    private readonly ITradingAutomationService _tradingPlan = Substitute.For<ITradingAutomationService>();
    private readonly IShipAssignmentRepository _assignments = Substitute.For<IShipAssignmentRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();

    public GameLoopServiceTests()
    {
        _serviceProvider = new ServiceCollection()
            .AddSingleton(_bus)
            .AddSingleton(_settings)
            .AddSingleton(_scoutPlan)
            .AddSingleton(_contractPlan)
            .AddSingleton(_probePlan)
            .AddSingleton(_miningPlan)
            .AddSingleton(_tradingPlan)
            .AddSingleton(_assignments)
            .AddSingleton(_ships)
            .AddSingleton(_goalExecutor)
            .BuildServiceProvider();

        _serviceScopeFactory.CreateScope().Returns(_ => _serviceProvider.CreateScope());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("SPECTER-DEBUG-3", "X1-PT96", "X1-PT96-H53", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15),
            new ShipModel("SPECTER-DEBUG-5", "X1-PT96", "X1-PT96-D42", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40),
        ]);
        _ships.FindAsync("SPECTER-DEBUG-5", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SPECTER-DEBUG-5", "X1-PT96", "X1-PT96-D42", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 40));
        _assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto(
                ShipSymbol: "SPECTER-DEBUG-5",
                AssignmentType: "Contract",
                OriginWaypoint: "X1-PT96-AST",
                DestWaypoint: "X1-PT96-D42",
                CargoSymbol: "IRON_ORE",
                ContractId: "CONTRACT-1",
                StepIndex: 0,
                AssignedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: null,
                RequiredUnits: 40),
        ]);
    }

    public void Dispose() => _serviceProvider.Dispose();

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
        SwitchOn("Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled", "Automation.Plan.ProbeDeployment.Enabled", "Automation.Plan.Mining.Enabled", "Automation.Plan.Trading.Enabled");

        await TickAsync();

        await _scoutPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _contractPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _probePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _miningPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _tradingPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _bus.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_BootstrapsOnlyThePlansThatAreSwitchedOn()
    {
        SwitchOn("Automation.Enabled", "Automation.Plan.Scout.Enabled", "Automation.Plan.Contract.Enabled");

        await TickAsync();

        await _scoutPlan.Received(1).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _contractPlan.Received(1).EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _probePlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _miningPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
        await _tradingPlan.DidNotReceive().EnsureBootstrappedAsync(Arg.Any<CancellationToken>());
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

    private void SwitchOn(params string[] keys)
    {
        foreach (var key in keys)
        {
            _settings.GetAsync<bool>(key, Arg.Any<CancellationToken>()).Returns(true);
        }
    }

    private async Task TickAsync()
    {
        var apiAvailability = Substitute.For<IApiAvailabilityState>();
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        using var sut = new GameLoopService(
            _serviceScopeFactory,
            apiAvailability,
            leaderElection,
            NullLogger<GameLoopService>.Instance);

        var tickMethod = typeof(GameLoopService)
            .GetMethod("TickAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)tickMethod.Invoke(sut, [CancellationToken.None])!;
    }
}

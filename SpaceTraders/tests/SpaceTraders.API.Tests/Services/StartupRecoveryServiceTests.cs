using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// D26: a restart reconsiders the contract's ships once, instead of waiting for their deliveries.
/// B38: only a ship that is in transit is recovered as one.
/// </summary>
public sealed class StartupRecoveryServiceTests
{
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IContractPlanService _contractPlan = Substitute.For<IContractPlanService>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalExecutorService _goalExecutor = Substitute.For<IShipGoalExecutorService>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IActiveReset _reset = Substitute.For<IActiveReset>();
    private readonly ShipNameBook _names;

    public StartupRecoveryServiceTests()
    {
        _settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(true);
        _reset.ResetDate.Returns("2026-10-04");
        _names = new ShipNameBook(_reset);
    }

    [Fact]
    public async Task StartAsync_TellsTheNameBookTheFleet_SoTheFirstStepsLinesCarryTheNames()
    {
        // Slice 2.14 (D72): until the metrics' first sample, 10 seconds on, the log lines would go without them.
        Fleet(Ship("DOCKED", DateTimeOffset.UtcNow.AddMinutes(-5)));

        await StartAsync();

        _names.NameOf("SPECTER-1").Should().Be(ShipNames.For([Ship("DOCKED", DateTimeOffset.UtcNow)], "2026-10-04")["SPECTER-1"]);
    }

    [Fact]
    public async Task StartAsync_ReleasesTheContractsShips_ForThePlansToAssignThemAgain()
    {
        // Asked for on 2026-10-02: the command ship, mining for the contract, would otherwise survey
        // only once it had filled its hold.
        ContractPlanIs(on: true);

        await StartAsync();

        await _contractPlan.Received(1).ReleaseShipsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_WithTheContractPlanOff_LeavesItsShipsAlone()
    {
        // A plan that is switched off is left as it is: nothing would assign the ships again.
        ContractPlanIs(on: false);

        await StartAsync();

        await _contractPlan.DidNotReceiveWithAnyArgs().ReleaseShipsAsync(default);
    }

    [Fact]
    public async Task StartAsync_WithAutomationOff_LeavesTheContractsShipsAlone()
    {
        _settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(false);
        ContractPlanIs(on: true);

        await StartAsync();

        await _contractPlan.DidNotReceiveWithAnyArgs().ReleaseShipsAsync(default);
    }

    [Theory]
    [InlineData("DOCKED")]
    [InlineData("IN_ORBIT")]
    public async Task StartAsync_AShipThatIsNotInTransit_GetsItsGoalStep_ButIsNotReportedInTransit(string status)
    {
        // B38, seen on the cluster on 2026-10-04: at the restart of 18:09:17Z the bot logged that SPECTER-1 "arrived at"
        // X1-FJ91-H60, where it had been docked since 16:12Z, and published a ShipInTransitEvent for it, which writes an
        // "in transit" activity row; at 13:06:52Z it did the same for a new agent's two ships, which had never moved.
        // Startup sync stores the last route's arrival on every ship, so a docked or orbiting ship keeps an arrival time
        // in the past. So does a ship that arrived while the bot was down: the API has it in orbit, and the scheduler
        // fires its arrival.
        Fleet(Ship(status, arrivesAt: TimeProvider.System.GetUtcNow().AddHours(-2)));

        await StartAsync();

        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipInTransitEvent>(), Arg.Any<DeliveryOptions?>());
        await _goalExecutor.Received(1).ExecuteAsync("SPECTER-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_AShipInTransit_IsReportedInTransit_WithItsArrivalTime()
    {
        // Its goal goes on when the scheduler fires its arrival, so it gets no goal step now.
        var arrivesAt = TimeProvider.System.GetUtcNow().AddMinutes(10);
        Fleet(Ship("IN_TRANSIT", arrivesAt));

        await StartAsync();

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipInTransitEvent>(e => e.ShipSymbol == "SPECTER-1" && e.DestinationWaypointSymbol == "X1-FJ91-H60" && e.ArrivalTime == arrivesAt),
            Arg.Any<DeliveryOptions?>());
        await _goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_AShipStillInTransitAfterItsArrivalTime_IsReportedArrived_AndGetsItsGoalStep()
    {
        // The case the first branch is for (docs/HOW_IT_WORKS.md): still marked in transit, with its arrival time passed.
        // The repository lands such a ship in orbit when it lists the fleet, so on the cluster it is recovered as above.
        Fleet(Ship("IN_TRANSIT", arrivesAt: TimeProvider.System.GetUtcNow().AddMinutes(-1)));

        await StartAsync();

        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipInTransitEvent>(e => e.ShipSymbol == "SPECTER-1" && e.DestinationWaypointSymbol == "X1-FJ91-H60"),
            Arg.Any<DeliveryOptions?>());
        await _goalExecutor.Received(1).ExecuteAsync("SPECTER-1", Arg.Any<CancellationToken>());
    }

    private void ContractPlanIs(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Contract), Arg.Any<CancellationToken>()).Returns(on);

    private void Fleet(params ShipModel[] fleet) => _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

    /// <summary>SPECTER-1 as startup sync stores it: with the arrival of its last route, which went to X1-FJ91-H60.</summary>
    private static ShipModel Ship(string status, DateTimeOffset arrivesAt)
        => new("SPECTER-1", "X1-FJ91", "X1-FJ91-H60", status, "CRUISE", 400, 400, arrivesAt, DestWaypointSymbol: "X1-FJ91-H60");

    private async Task StartAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_settings);
        services.AddSingleton(_contractPlan);
        services.AddSingleton(_ships);
        services.AddSingleton(_goalExecutor);
        services.AddSingleton(_bus);
        await using var provider = services.BuildServiceProvider();

        await new StartupRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), _names, NullLogger<StartupRecoveryService>.Instance)
            .StartAsync(CancellationToken.None);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using Wolverine;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// D26: a restart reconsiders the contract's ships once, instead of waiting for their deliveries.
/// </summary>
public sealed class StartupRecoveryServiceTests
{
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IContractPlanService _contractPlan = Substitute.For<IContractPlanService>();

    public StartupRecoveryServiceTests()
    {
        _settings.GetAsync<bool>(AutomationSwitches.EnabledSetting, Arg.Any<CancellationToken>()).Returns(true);
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

    private void ContractPlanIs(bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(AutomationPlan.Contract), Arg.Any<CancellationToken>()).Returns(on);

    private async Task StartAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_settings);
        services.AddSingleton(_contractPlan);
        services.AddSingleton(Substitute.For<IShipRepository>());
        services.AddSingleton(Substitute.For<IShipGoalExecutorService>());
        services.AddSingleton(Substitute.For<IMessageBus>());
        await using var provider = services.BuildServiceProvider();

        await new StartupRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<StartupRecoveryService>.Instance)
            .StartAsync(CancellationToken.None);
    }
}

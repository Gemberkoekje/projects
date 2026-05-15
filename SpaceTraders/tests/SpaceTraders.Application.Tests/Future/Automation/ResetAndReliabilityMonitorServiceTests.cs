using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ResetAndReliabilityMonitorServiceTests
{
    private static ServiceProvider BuildProvider(
        ISettingsRepository settings,
        ISpaceTradersPort port,
        IShipRepository ships,
        IContractRepository contracts,
        IMessageBus bus)
    {
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        services.AddSingleton(port);
        services.AddSingleton(ships);
        services.AddSingleton(contracts);
        services.AddSingleton(bus);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TickAsync_WhenResetIsNear_PublishesWarningAndPausesAutomation()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>("Reliability.PauseAutomationBeforeReset", Arg.Any<CancellationToken>()).Returns(true);
        settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(true);
        settings.GetAsync<bool>("Runtime.AutomationPausedByReset", Arg.Any<CancellationToken>()).Returns(false);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new ServerStatusModel(
                "ONLINE",
                "1",
                null,
                null,
                DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
                DateTimeOffset.UtcNow.AddMinutes(5)));
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>(Array.Empty<ShipModel>(), 0, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());

        var contracts = Substitute.For<IContractRepository>();
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Application.DTOs.ContractDto>());

        var bus = Substitute.For<IMessageBus>();

        await using var provider = BuildProvider(settings, port, ships, contracts, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        using var service = new ResetAndReliabilityMonitorService(scopeFactory, leaderElection, NullLogger<ResetAndReliabilityMonitorService>.Instance);
        await service.TickAsync(CancellationToken.None);

        await bus.Received().PublishAsync(Arg.Any<ServerResetWarningEvent>(), Arg.Any<DeliveryOptions>());
        await bus.Received().PublishAsync(Arg.Any<AutomationPausedForServerResetEvent>(), Arg.Any<DeliveryOptions>());
        await settings.Received().SetAsync("Automation.Enabled", "false", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TickAsync_WhenShipsAreStale_PublishesCacheDivergenceEvent()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new ServerStatusModel("ONLINE", "1", null, null, null, null));
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>(
            [
                new ShipModel(
                    "SHIP-OLD",
                    "X1-AB",
                    "X1-AB-A1",
                    "IN_ORBIT",
                    "CRUISE",
                    100,
                    100)
            ],
            1,
            1,
            20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipModel(
                "SHIP-OLD",
                "X1-AB",
                "X1-AB-A1",
                "IN_ORBIT",
                "CRUISE",
                100,
                100,
                LastSyncedAt: DateTimeOffset.UtcNow.AddHours(-1))
        ]);

        var contracts = Substitute.For<IContractRepository>();
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Application.DTOs.ContractDto>());

        var bus = Substitute.For<IMessageBus>();

        await using var provider = BuildProvider(settings, port, ships, contracts, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        using var service = new ResetAndReliabilityMonitorService(scopeFactory, leaderElection, NullLogger<ResetAndReliabilityMonitorService>.Instance);
        await service.TickAsync(CancellationToken.None);

        await bus.Received().PublishAsync(
            Arg.Is<CacheDivergenceDetectedEvent>(e => e.MismatchCount == 1),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task TickAsync_WhenNotLeader_DoesNothing()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(false);

        var settings = Substitute.For<ISettingsRepository>();
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var bus = Substitute.For<IMessageBus>();

        await using var provider = BuildProvider(settings, port, ships, contracts, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        using var service = new ResetAndReliabilityMonitorService(scopeFactory, leaderElection, NullLogger<ResetAndReliabilityMonitorService>.Instance);
        await service.TickAsync(CancellationToken.None);

        await port.DidNotReceiveWithAnyArgs().GetStatusAsync(default);
        await bus.DidNotReceiveWithAnyArgs().PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
        await settings.DidNotReceiveWithAnyArgs().SetAsync<string>(default!, default!, default);
    }

    [Fact]
    public async Task TickAsync_WhenStatusProbeFails_SetsApiUnavailableAlert()
    {
        var leaderElection = Substitute.For<ILeaderElection>();
        leaderElection.IsLeader.Returns(true);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetStatusAsync(Arg.Any<CancellationToken>()).Returns<Task<ServerStatusModel>>(_ => throw new HttpRequestException("down"));
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>(Array.Empty<ShipModel>(), 0, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());

        var contracts = Substitute.For<IContractRepository>();
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<Application.DTOs.ContractDto>());

        var bus = Substitute.For<IMessageBus>();

        await using var provider = BuildProvider(settings, port, ships, contracts, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        using var service = new ResetAndReliabilityMonitorService(scopeFactory, leaderElection, NullLogger<ResetAndReliabilityMonitorService>.Instance);
        await service.TickAsync(CancellationToken.None);

        await settings.Received().SetAsync("Runtime.Alert.ApiUnavailable", "true", Arg.Any<CancellationToken>());
    }
}

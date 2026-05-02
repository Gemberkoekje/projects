using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Sync;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.API.Tests.Services;

public sealed class StartupRecoveryServiceTests
{
    private static ServiceProvider BuildProvider(
        ISpaceTradersPort port,
        IShipRepository ships,
        ISettingsRepository settings,
        IMessageBus bus,
        IShipGoalExecutorService goalExecutor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(port);
        services.AddSingleton(ships);
        services.AddSingleton(settings);
        services.AddSingleton(bus);
        services.AddSingleton(goalExecutor);
        services.AddSingleton<ILogger<SyncAllShipsHandler>>(NullLogger<SyncAllShipsHandler>.Instance);
        services.AddSingleton<SyncAllShipsHandler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartAsync_WhenAutomationDisabled_SkipsRecoveryEmission()
    {
        var syncedShip = new ShipModel("SHIP-1", "X1-AB", "X1-AB-DOCK", "DOCKED", "CRUISE", 100, 100, null, null);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>([syncedShip], 1, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([syncedShip]);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(false);

        var bus = Substitute.For<IMessageBus>();
        var goalExecutor = Substitute.For<IShipGoalExecutorService>();

        using var provider = BuildProvider(port, ships, settings, bus, goalExecutor);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new StartupRecoveryService(scopeFactory, NullLogger<StartupRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipInTransitEvent>(), Arg.Any<DeliveryOptions>());
        await goalExecutor.DidNotReceive().ExecuteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_WhenAutomationEnabled_ExecutesGoalForDockedAndInOrbitShips()
    {
        var docked = new ShipModel("SHIP-1", "X1-AB", "X1-AB-DOCK", "DOCKED", "CRUISE", 100, 100, null, null);
        var inOrbit = new ShipModel("SHIP-2", "X1-AB", "X1-AB-ORBIT", "IN_ORBIT", "CRUISE", 100, 100, null, null);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>([docked, inOrbit], 1, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([docked, inOrbit]);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>()).Returns(true);

        var bus = Substitute.For<IMessageBus>();
        var goalExecutor = Substitute.For<IShipGoalExecutorService>();

        using var provider = BuildProvider(port, ships, settings, bus, goalExecutor);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new StartupRecoveryService(scopeFactory, NullLogger<StartupRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await goalExecutor.Received(1).ExecuteAsync("SHIP-1", Arg.Any<CancellationToken>());
        await goalExecutor.Received(1).ExecuteAsync("SHIP-2", Arg.Any<CancellationToken>());
    }
}

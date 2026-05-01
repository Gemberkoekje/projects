using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
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
        IMessageBus bus)
    {
        var services = new ServiceCollection();
        services.AddSingleton(port);
        services.AddSingleton(ships);
        services.AddSingleton(settings);
        services.AddSingleton(bus);
        services.AddSingleton<ILogger<SyncAllShipsHandler>>(NullLogger<SyncAllShipsHandler>.Instance);
        services.AddSingleton<SyncAllShipsHandler>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartAsync_WhenAutomationDisabled_SkipsRecoveryEmission()
    {
        var syncedShip = new ShipModel(
            "SHIP-1",
            "X1-AB",
            "X1-AB-DOCK",
            "DOCKED",
            "CRUISE",
            100,
            100,
            null,
            null);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>([syncedShip], 1, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([syncedShip]);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>())
            .Returns(false);

        var bus = Substitute.For<IMessageBus>();

        using var provider = BuildProvider(port, ships, settings, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new StartupRecoveryService(scopeFactory, NullLogger<StartupRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipInTransitEvent>(), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipDockedEvent>(), Arg.Any<DeliveryOptions>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ShipInOrbitEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task StartAsync_WhenAutomationEnabled_EmitsRecoveryEvents()
    {
        var docked = new ShipModel("SHIP-1", "X1-AB", "X1-AB-DOCK", "DOCKED", "CRUISE", 100, 100, null, null);
        var inOrbit = new ShipModel("SHIP-2", "X1-AB", "X1-AB-ORBIT", "IN_ORBIT", "CRUISE", 100, 100, null, null);

        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>([docked, inOrbit], 1, 1, 20));

        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([docked, inOrbit]);

        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<bool>("Automation.Enabled", Arg.Any<CancellationToken>())
            .Returns(true);

        var bus = Substitute.For<IMessageBus>();

        using var provider = BuildProvider(port, ships, settings, bus);
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var service = new StartupRecoveryService(scopeFactory, NullLogger<StartupRecoveryService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await bus.Received().PublishAsync(Arg.Any<ShipDockedEvent>(), Arg.Any<DeliveryOptions>());
        await bus.Received().PublishAsync(Arg.Any<ShipInOrbitEvent>(), Arg.Any<DeliveryOptions>());
    }
}

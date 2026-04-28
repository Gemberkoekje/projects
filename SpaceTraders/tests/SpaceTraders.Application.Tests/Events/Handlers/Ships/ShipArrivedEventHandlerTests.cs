using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Events.Handlers.Ships;

public sealed class ShipArrivedEventHandlerTests
{
    private static ShipArrivedEvent MakeEvent(string waypoint = "X1-AB-009") =>
        new("SHIP-1", "X1-AB", waypoint, DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow);

    private static ServiceCollection BaseServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IMessageBus>());
        services.AddSingleton(Substitute.For<IShipAssignmentRepository>());
        services.AddSingleton(Substitute.For<IShipRepository>());
        services.AddSingleton(Substitute.For<IAgentRepository>());
        services.AddSingleton(Substitute.For<ISpaceTradersPort>());
        services.AddSingleton(Substitute.For<IWaypointRepository>());
        services.AddSingleton(Substitute.For<IMarketRepository>());
        services.AddSingleton(Substitute.For<IShipyardRepository>());
        services.AddSingleton<IWaypointVisitService, WaypointVisitService>();
        services.AddSingleton<IMarketRefreshService, MarketRefreshService>();
        services.AddSingleton<IShipyardRefreshService, ShipyardRefreshService>();
        services.AddSingleton<IInOrbitCommandAcceptor>(Substitute.For<IInOrbitCommandAcceptor>());
        return services;
    }

    [Fact]
    public async Task DispatchAsync_WithShipArrivedEvent_RoutesViaInOrbitHandlers()
    {
        var services = BaseServices();
        var assignments = services.First(d => d.ServiceType == typeof(IShipAssignmentRepository)).ImplementationInstance as IShipAssignmentRepository;
        var bus = services.First(d => d.ServiceType == typeof(IMessageBus)).ImplementationInstance as IMessageBus;

        assignments!.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipAssignmentDto?)null);

        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitTraderEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus!, NullLogger<ChainOfCommandDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync<ShipInOrbitEvent>(MakeEvent(), CancellationToken.None);

        result.Outcome.Should().Be("Handled");
        result.HandlerName.Should().Be(nameof(ShipInOrbitEventHandler));
    }

    [Fact]
    public async Task DispatchAsync_WithScoutAssignment_UsesScoutInOrbitHandler()
    {
        var services = BaseServices();
        var assignments = services.First(d => d.ServiceType == typeof(IShipAssignmentRepository)).ImplementationInstance as IShipAssignmentRepository;
        var bus = services.First(d => d.ServiceType == typeof(IMessageBus)).ImplementationInstance as IMessageBus;
        var ships = services.First(d => d.ServiceType == typeof(IShipRepository)).ImplementationInstance as IShipRepository;

        assignments!.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1", "Scout", "X1-AB-009", null, null, null, 0, DateTimeOffset.UtcNow, null, 0));

        ships!.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-009", "IN_ORBIT", "CRUISE", 80, 100));

        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitTraderEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus!, NullLogger<ChainOfCommandDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync<ShipInOrbitEvent>(MakeEvent("X1-AB-009"), CancellationToken.None);

        result.Outcome.Should().Be("Handled");
        result.HandlerName.Should().Be(nameof(ShipInOrbitScoutEventHandler));
    }

    [Fact]
    public async Task DispatchAsync_WithMineAssignmentAtOrigin_UsesMineInOrbitHandler()
    {
        var services = BaseServices();
        var assignments = services.First(d => d.ServiceType == typeof(IShipAssignmentRepository)).ImplementationInstance as IShipAssignmentRepository;
        var bus = services.First(d => d.ServiceType == typeof(IMessageBus)).ImplementationInstance as IMessageBus;
        var ships = services.First(d => d.ServiceType == typeof(IShipRepository)).ImplementationInstance as IShipRepository;
        var inOrbitCommands = services.First(d => d.ServiceType == typeof(IInOrbitCommandAcceptor)).ImplementationInstance as IInOrbitCommandAcceptor;

        assignments!.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null, 0));

        ships!.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100,
                CargoCurrent: 0, CargoCapacity: 40));

        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitTraderEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus!, NullLogger<ChainOfCommandDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync<ShipInOrbitEvent>(MakeEvent("X1-AB-AST"), CancellationToken.None);

        result.Outcome.Should().Be("Handled");
        result.HandlerName.Should().Be(nameof(ShipInOrbitMineEventHandler));

        await inOrbitCommands!.Received(1).ExtractAsync("SHIP-1", Arg.Any<CancellationToken>());
    }
}

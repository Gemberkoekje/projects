using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class ShipUndockedEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_WithShipUndockedEvent_RoutesViaTraderInOrbitHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var waypointVisit = Substitute.For<IWaypointVisitService>();
        var markets = Substitute.For<IMarketRefreshService>();
        var shipyards = Substitute.For<IShipyardRefreshService>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-010", "X1-AB-099", "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null, 0));
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100, CargoCurrent: 0, CargoCapacity: 20));

        services.AddSingleton(bus);
        services.AddSingleton(assignments);
        services.AddSingleton(ships);
        services.AddSingleton(inOrbit);
        services.AddSingleton(Substitute.For<IAgentRepository>());
        services.AddSingleton(Substitute.For<ISpaceTradersPort>());
        services.AddSingleton(waypointVisit);
        services.AddSingleton(markets);
        services.AddSingleton(shipyards);
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitTraderEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync<ShipInOrbitEvent>(
            new ShipUndockedEvent("SHIP-1", "X1-AB", "X1-AB-001", Guid.NewGuid(), Guid.Empty, DateTimeOffset.UtcNow),
            CancellationToken.None);

        result.Outcome.Should().Be("Handled");
        result.HandlerName.Should().Be(nameof(ShipInOrbitTraderEventHandler));
        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-010", Arg.Any<CancellationToken>());
    }
}

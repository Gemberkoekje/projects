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

public sealed class ShipArrivedEventHandlerTests
{
    [Fact]
    public async Task DispatchAsync_UsesScoutArrivalHandler_WhenScoutAssignmentExists()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1",
                "Scout",
                "X1-AB-009",
                null,
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        waypoints.FindAsync("X1-AB-009", Arg.Any<CancellationToken>())
            .Returns(new WaypointCacheModel("X1-AB-009", "X1-AB", "PLANET", 0, 0, true, true, DateTimeOffset.UtcNow));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(assignments);
        services.AddSingleton(waypoints);
        services.AddSingleton(markets);
        services.AddSingleton(shipyards);
        services.AddSingleton(ships);
        services.AddSingleton(agents);
        services.AddSingleton(port);
        services.AddSingleton<IWaypointVisitService, WaypointVisitService>();
        services.AddSingleton<IMarketRefreshService, MarketRefreshService>();
        services.AddSingleton<IShipyardRefreshService, ShipyardRefreshService>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipArrivedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-009",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipArrivedScoutEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleEvent));

        await waypoints.Received(1).MarkVisitedAsync("X1-AB-009", Arg.Any<CancellationToken>());
        await bus.Received(1).SendAsync(Arg.Is<SpaceTraders.Application.Commands.Sync.RefreshMarketDataCommand>(c => c.WaypointSymbol == "X1-AB-009"));
        await bus.Received(1).SendAsync(Arg.Is<SpaceTraders.Application.Commands.Sync.RefreshShipyardDataCommand>(c => c.WaypointSymbol == "X1-AB-009"));
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason == "Scout arrival handled; cache refresh requested."),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_UsesMineArrivalHandler_WhenMineAssignmentExists()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto(
                "SHIP-1",
                "Mine",
                "X1-AB-AST",
                "X1-AB-MKT",
                null,
                null,
                0,
                DateTimeOffset.UtcNow,
                null,
                0));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "DOCKED", "CRUISE", 80, 100));

        port.OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NavModel("IN_ORBIT", "X1-AB", "X1-AB-AST", "CRUISE", null, null));

        port.ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ExtractionActionResult("IRON_ORE", 10, new CargoModel(10, 100), 60));

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(assignments);
        services.AddSingleton(waypoints);
        services.AddSingleton(markets);
        services.AddSingleton(shipyards);
        services.AddSingleton(ships);
        services.AddSingleton(agents);
        services.AddSingleton(port);
        services.AddSingleton<IWaypointVisitService, WaypointVisitService>();
        services.AddSingleton<IMarketRefreshService, MarketRefreshService>();
        services.AddSingleton<IShipyardRefreshService, ShipyardRefreshService>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipArrivedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-AST",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipArrivedMineEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleEvent));

        await port.Received(1).OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await port.Received(1).ExtractResourcesAsync("SHIP-1", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason == "Mine arrival extracted resources."),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DispatchAsync_UsesFallbackArrivalHandler_WhenNoRoleSpecificAssignmentMatches()
    {
        var bus = Substitute.For<IMessageBus>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var ships = Substitute.For<IShipRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var port = Substitute.For<ISpaceTradersPort>();

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns((ShipAssignmentDto?)null);

        var services = new ServiceCollection();
        services.AddSingleton(bus);
        services.AddSingleton(assignments);
        services.AddSingleton(waypoints);
        services.AddSingleton(markets);
        services.AddSingleton(shipyards);
        services.AddSingleton(ships);
        services.AddSingleton(agents);
        services.AddSingleton(port);
        services.AddSingleton<IWaypointVisitService, WaypointVisitService>();
        services.AddSingleton<IMarketRefreshService, MarketRefreshService>();
        services.AddSingleton<IShipyardRefreshService, ShipyardRefreshService>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedScoutEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedMineEventHandler>();
        services.AddSingleton<IChainOfCommandEventHandler<ShipArrivedEvent>, ShipArrivedEventHandler>();

        await using var provider = services.BuildServiceProvider();
        var dispatcher = new ChainOfCommandDispatcher(provider, bus, NullLogger<ChainOfCommandDispatcher>.Instance);

        var @event = new ShipArrivedEvent(
            "SHIP-1",
            "X1-AB",
            "X1-AB-777",
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            Guid.Empty,
            DateTimeOffset.UtcNow);

        var result = await dispatcher.DispatchAsync(@event, CancellationToken.None);

        result.HandlerName.Should().Be(nameof(ShipArrivedEventHandler));
        result.Outcome.Should().Be("Handled");
        result.NextEventType.Should().Be(nameof(ShipIdleEvent));

        await bus.Received(1).PublishAsync(
            Arg.Is<ShipIdleEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.Reason == "No specialized arrival handler matched."),
            Arg.Any<DeliveryOptions>());
    }
}

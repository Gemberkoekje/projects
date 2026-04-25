using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class InitialAssignmentHandlerTests
{
    private static ISettingsRepository MakeSettings(int minProfit = 200, int maxDistance = 5)
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("Trade.MinProfitPerUnit", Arg.Any<CancellationToken>()).Returns(minProfit);
        settings.GetAsync<int>("Trade.MaxHaulDistance", Arg.Any<CancellationToken>()).Returns(maxDistance);
        return settings;
    }

    [Fact]
    public async Task Handle_AssignsTradeRoute_WhenRouteAvailable()
    {
        var routes = Substitute.For<ITradeOpportunityRepository>();
        routes.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new TradeOpportunityDto(1, "IRON_ORE", "X1-AB-001", "X1-AB-002", 100, 400, 300, 1, 300m, DateTimeOffset.UtcNow));

        var bus = Substitute.For<IMessageBus>();

        var handler = new InitialAssignmentHandler(routes, MakeSettings(), bus,
            NullLogger<InitialAssignmentHandler>.Instance);

        await handler.Handle(
            new NewShipPurchasedEvent("SHIP-2", ShipType.ShipMiningDrone, 50_000),
            CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c =>
                c.ShipSymbol == "SHIP-2" &&
                c.AssignmentType == "Trade" &&
                c.CargoSymbol == "IRON_ORE"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_AssignsIdle_WhenNoRouteAvailable()
    {
        var routes = Substitute.For<ITradeOpportunityRepository>();
        routes.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((TradeOpportunityDto?)null);

        var bus = Substitute.For<IMessageBus>();

        var handler = new InitialAssignmentHandler(routes, MakeSettings(), bus,
            NullLogger<InitialAssignmentHandler>.Instance);

        await handler.Handle(
            new NewShipPurchasedEvent("SHIP-2", ShipType.ShipMiningDrone, 50_000),
            CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipCommand>(c => c.ShipSymbol == "SHIP-2" && c.AssignmentType == "Idle"),
            Arg.Any<DeliveryOptions>());
    }
}

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class FleetExpansionDecisionHandlerTests
{
    private static AgentModel MakeAgent(long credits, int shipCount = 1) =>
        new("AGENT-1", null, null, credits, "COSMIC", shipCount);

    private static ISettingsRepository MakeSettings(int maxShips = 5, long reserve = 100_000, string shipType = "SHIP_MINING_DRONE")
    {
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(maxShips);
        settings.GetAsync<long>("FleetExpansion.MinCreditReserve", Arg.Any<CancellationToken>()).Returns(reserve);
        settings.GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>()).Returns(shipType);
        return settings;
    }

    [Fact]
    public async Task Handle_IssuesPurchaseCommand_WhenConditionsMet()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeAgent(500_000, shipCount: 1));

        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns("X1-AB-SHIPYARD");

        var bus = Substitute.For<IMessageBus>();
        var settings = MakeSettings(maxShips: 5, reserve: 100_000);

        var handler = new FleetExpansionDecisionHandler(agents, ships, shipyards, settings, bus,
            NullLogger<FleetExpansionDecisionHandler>.Instance);

        await handler.Handle(
            new ShipCargoSoldEvent("SHIP-1", new("IRON_ORE"), 10, 50_000, 500_000),
            CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<PurchaseShipCommand>(c => c.ShipType == "SHIP_MINING_DRONE" && c.ShipyardWaypoint == "X1-AB-SHIPYARD"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_SkipsPurchase_WhenFleetAtCap()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeAgent(500_000, shipCount: 5));

        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var bus = Substitute.For<IMessageBus>();
        var settings = MakeSettings(maxShips: 5);

        var handler = new FleetExpansionDecisionHandler(agents, ships, shipyards, settings, bus,
            NullLogger<FleetExpansionDecisionHandler>.Instance);

        await handler.Handle(
            new ShipCargoSoldEvent("SHIP-1", new("IRON_ORE"), 10, 50_000, 500_000),
            CancellationToken.None);

        await bus.DidNotReceive().SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_SkipsPurchase_WhenCreditsAtReserve()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeAgent(100_000, shipCount: 1));

        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var bus = Substitute.For<IMessageBus>();
        var settings = MakeSettings(reserve: 100_000);

        var handler = new FleetExpansionDecisionHandler(agents, ships, shipyards, settings, bus,
            NullLogger<FleetExpansionDecisionHandler>.Instance);

        await handler.Handle(
            new ShipCargoSoldEvent("SHIP-1", new("IRON_ORE"), 10, 50_000, 100_000),
            CancellationToken.None);

        await bus.DidNotReceive().SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_SkipsPurchase_WhenNoShipyardKnown()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeAgent(500_000, shipCount: 1));

        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var bus = Substitute.For<IMessageBus>();
        var settings = MakeSettings();

        var handler = new FleetExpansionDecisionHandler(agents, ships, shipyards, settings, bus,
            NullLogger<FleetExpansionDecisionHandler>.Instance);

        await handler.Handle(
            new ShipCargoSoldEvent("SHIP-1", new("IRON_ORE"), 10, 50_000, 500_000),
            CancellationToken.None);

        await bus.DidNotReceive().SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_AlsoTriggersOnContractFulfilled()
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(MakeAgent(500_000, shipCount: 1));

        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns("X1-AB-SHIPYARD");

        var bus = Substitute.For<IMessageBus>();
        var settings = MakeSettings();

        var handler = new FleetExpansionDecisionHandler(agents, ships, shipyards, settings, bus,
            NullLogger<FleetExpansionDecisionHandler>.Instance);

        await handler.Handle(new ContractFulfilledEvent("CONTRACT-1", 200_000), CancellationToken.None);

        await bus.Received(1).SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }
}

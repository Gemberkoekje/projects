using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Services;

public sealed class ShipPurchaseServiceTests
{
    [Fact]
    public async Task TryPurchaseAsync_PublishesThePurchase_ForTheLedgerAndTheCredits()
    {
        // B7: a ship purchase changed the cached credits, but nothing published it, so the ledger and
        // the credits-spent metric never saw the biggest expense there is.
        var port = Substitute.For<ISpaceTradersPort>();
        var agents = Substitute.For<IAgentRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var budget = Substitute.For<IBudgetPolicy>();
        var bus = Substitute.For<IMessageBus>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 100_000, "COSMIC", 1));
        shipyards.FindByWaypointAsync("X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new ShipyardWaypointDto
            {
                WaypointSymbol = "X1-AB-SY1",
                SystemSymbol = "X1-AB",
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships = [new ShipyardShipDto { Type = "SHIP_MINING_DRONE", PurchasePrice = 12_000 }],
            });
        budget.EvaluateAsync(12_000, Arg.Any<CancellationToken>()).Returns(new BudgetDecision(true, 100_000, 20_000, 80_000));
        port.PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                new AgentModel("AGENT", null, "X1-AB-HQ", 88_000, "COSMIC", 2),
                "AGENT-2",
                new NavModel("DOCKED", "X1-AB", "X1-AB-SY1", "CRUISE", null, null),
                new FuelModel(80, 80),
                new CargoModel(0, 15, []),
                12_000));
        var service = new ShipPurchaseService(
            port,
            agents,
            Substitute.For<IShipRepository>(),
            shipyards,
            budget,
            bus,
            NullLogger<ShipPurchaseService>.Instance);

        var result = await service.TryPurchaseAsync("SHIP_MINING_DRONE", "X1-AB-SY1");

        result.IsSuccess.Should().BeTrue();
        await bus.Received(1).PublishAsync(
            Arg.Is<NewShipPurchasedEvent>(e => e.ShipSymbol == "AGENT-2" && e.Type == ShipType.ShipMiningDrone && e.CostPaid == 12_000),
            Arg.Any<DeliveryOptions>());
        await bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 100_000 && e.NewCredits == 88_000),
            Arg.Any<DeliveryOptions>());
    }

    [Theory]
    [InlineData("SHIP_MINING_DRONE", ShipType.ShipMiningDrone)]
    [InlineData("SHIP_PROBE", ShipType.ShipProbe)]
    [InlineData("SHIP_LIGHT_HAULER", ShipType.ShipLightHauler)]
    [InlineData("SHIP_SOMETHING_NEW", ShipType.None)]
    public void ToShipType_ReadsTheApisShipTypes(string apiType, ShipType expected)
    {
        ShipPurchaseService.ToShipType(apiType).Should().Be(expected);
    }
}

public sealed class AgentCreditsUpdatesTests
{
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    [Fact]
    public async Task SetCreditsAsync_PublishesTheChange()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 175_000, "COSMIC", 2));

        await _agents.SetCreditsAsync(_bus, 174_280, CancellationToken.None);

        await _agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(a => a.Credits == 174_280), Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 175_000 && e.NewCredits == 174_280),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SetCreditsAsync_PublishesNothing_WhenTheCreditsDidNotChange()
    {
        _agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 175_000, "COSMIC", 2));

        await _agents.SetCreditsAsync(_bus, 175_000, CancellationToken.None);

        await _bus.DidNotReceive().PublishAsync(Arg.Any<AgentCreditsChangedEvent>(), Arg.Any<DeliveryOptions>());
    }
}

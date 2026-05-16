using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class PurchaseShipHandlerTests
{
    [Fact]
    public async Task Handle_PersistsCargoReturnedByPurchase()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var agents = Substitute.For<IAgentRepository>();
        var ships = Substitute.For<IShipRepository>();
        var shipyards = Substitute.For<IShipyardRepository>();
        var budget = Substitute.For<IBudgetPolicy>();
        var bus = Substitute.For<IMessageBus>();

        shipyards.FindByWaypointAsync("X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new ShipyardWaypointDto
            {
                WaypointSymbol = "X1-AB-SY1",
                SystemSymbol = "X1-AB",
                ShipTypes = ["SHIP_MINING_DRONE"],
                Ships =
                [
                    new ShipyardShipDto
                    {
                        Type = "SHIP_MINING_DRONE",
                        PurchasePrice = 12_000,
                    }
                ]
            });

        budget.EvaluateAsync(12_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(true, 100_000, 20_000, 80_000));

        port.PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                new AgentModel("AGENT", null, "X1-AB-HQ", 88_000, "FACTION", 2),
                "DRONE-1",
                new NavModel("DOCKED", "X1-AB", "X1-AB-SY1", "CRUISE", null, null),
                new FuelModel(20, 20),
                new CargoModel(0, 20, []),
                12_000));

        var sut = new PurchaseShipHandler(
            port,
            agents,
            ships,
            shipyards,
            budget,
            bus,
            NullLogger<PurchaseShipHandler>.Instance);

        await sut.Handle(new PurchaseShipCommand("SHIP_MINING_DRONE", "X1-AB-SY1"), CancellationToken.None);

        await ships.Received(1).UpsertAsync(
            Arg.Is<ShipModel>(ship =>
                ship.Symbol == "DRONE-1"
                && ship.ShipType == "SHIP_MINING_DRONE"
                && ship.CargoCurrent == 0
                && ship.CargoCapacity == 20),
            Arg.Any<CancellationToken>());
    }
}

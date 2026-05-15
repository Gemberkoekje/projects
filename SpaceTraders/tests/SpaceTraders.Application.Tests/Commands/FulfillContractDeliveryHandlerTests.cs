using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class FulfillContractDeliveryHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_DeliversCargo_AndFulfills_WhenAllDeliverablesCompleted()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var dock = Substitute.For<IDockSubCommand>();
        var orbit = Substitute.For<IOrbitSubCommand>();
        var navigate = Substitute.For<INavigateSubCommand>();
        var bus = Substitute.For<Wolverine.IMessageBus>();

        var ship = new ShipModel(
            Symbol: "SHIP-1",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-MKT",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 80,
            FuelCapacity: 100,
            CargoCurrent: 10,
            CargoCapacity: 40,
            CargoInventory: [new CargoItemModel("IRON_ORE", 10)]);

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship, ship with { CargoCurrent = 0, CargoInventory = [] });

        port.DeliverContractAsync("C-1", "SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-1",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 10, 10)],
                AgentSymbol: null,
                AgentCredits: null,
                ShipCargo: new CargoModel(0, 40, [])));

        contracts.FindAsync("C-1", Arg.Any<CancellationToken>()).Returns(new ContractDto(
            Id: "C-1",
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto>
            {
                new("IRON_ORE", "X1-AB-MKT", 10, 10),
            })));

        port.FulfillContractAsync("C-1", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-1",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: true,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 10, 10)],
                AgentSymbol: null,
                AgentCredits: 1000,
                ShipCargo: null));

        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            dock,
            orbit,
            navigate,
            bus,
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        var result = await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-1", "C-1", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await port.Received(1).DeliverContractAsync("C-1", "SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
        await port.Received(1).FulfillContractAsync("C-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NavigatesToDestination_WhenShipNotAtDestination()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var dock = Substitute.For<IDockSubCommand>();
        var orbit = Substitute.For<IOrbitSubCommand>();
        var navigate = Substitute.For<INavigateSubCommand>();
        var bus = Substitute.For<Wolverine.IMessageBus>();

        ships.FindAsync("SHIP-2", Arg.Any<CancellationToken>()).Returns(new ShipModel(
            Symbol: "SHIP-2",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-AST",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 90,
            FuelCapacity: 100,
            CargoCurrent: 5,
            CargoCapacity: 40,
            CargoInventory: [new CargoItemModel("IRON_ORE", 5)]));

        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            dock,
            orbit,
            navigate,
            bus,
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        var result = await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-2", "C-2", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await navigate.Received(1).ExecuteAsync("SHIP-2", "X1-AB-MKT", Guid.Empty, Arg.Any<CancellationToken>());
        await port.DidNotReceiveWithAnyArgs().DeliverContractAsync(default!, default!, default!, default, default);
    }
}

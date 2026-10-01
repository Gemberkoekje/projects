using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class FulfillContractDeliveryHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_AFulfilment_IsPublished_WithItsPayment()
    {
        // B7: the fulfilment payment reached the cached credits (B33) but not the ledger or the metrics.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var bus = Substitute.For<IMessageBus>();
        var agents = Substitute.For<IAgentRepository>();
        var log = new LogRecorder();
        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 100, CargoCurrent: 0, CargoCapacity: 40, CargoInventory: []);
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        contracts.FindAsync("C-1", Arg.Any<CancellationToken>()).Returns(Contract("C-1", required: 10, fulfilled: 10));
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("AGENT", null, "X1-AB-HQ", 125_000, "COSMIC", 2));
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
                AgentSymbol: "AGENT",
                AgentCredits: 131_620,
                ShipCargo: null)
            {
                PaymentOnFulfilled = 6_620,
            });

        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            bus,
            agents,
            log.For<FulfillContractDeliveryHandler>());

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-1", "C-1", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        log.Journal.Should().ContainSingle().Which.Message.Should().Be("ContractFulfilled: contract C-1 fulfilled; it paid 6620 credits.");
        await bus.Received(1).PublishAsync(
            Arg.Is<ContractFulfilledEvent>(e => e.ContractId == "C-1" && e.Payment == 6_620),
            Arg.Any<DeliveryOptions>());
        await bus.Received(1).PublishAsync(
            Arg.Is<AgentCreditsChangedEvent>(e => e.OldCredits == 125_000 && e.NewCredits == 131_620),
            Arg.Any<DeliveryOptions>());
    }

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

        // The cached contract before the delivery, then after it.
        contracts.FindAsync("C-1", Arg.Any<CancellationToken>()).Returns(Contract("C-1", required: 10, fulfilled: 0), Contract("C-1", required: 10, fulfilled: 10));

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
            Substitute.For<IAgentRepository>(),
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
            Substitute.For<IAgentRepository>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        var result = await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-2", "C-2", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await navigate.Received(1).ExecuteAsync("SHIP-2", "X1-AB-MKT", Guid.Empty, Arg.Any<CancellationToken>());
        await port.DidNotReceiveWithAnyArgs().DeliverContractAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_DeliversAtMostTheUnitsTheContractStillNeeds()
    {
        // A trip's last extraction can bring more aboard than the contract still needs. The surplus
        // earns nothing, and the API may refuse the whole delivery for it.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ship = new ShipModel(
            Symbol: "SHIP-3",
            SystemSymbol: "X1-AB",
            WaypointSymbol: "X1-AB-MKT",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 80,
            FuelCapacity: 80,
            CargoCurrent: 5,
            CargoCapacity: 15,
            CargoInventory: [new CargoItemModel("IRON_ORE", 5)]);
        ships.FindAsync("SHIP-3", Arg.Any<CancellationToken>()).Returns(ship);
        contracts.FindAsync("C-3", Arg.Any<CancellationToken>()).Returns(Contract("C-3", required: 42, fulfilled: 39), Contract("C-3", required: 42, fulfilled: 42));
        port.DeliverContractAsync("C-3", "SHIP-3", "IRON_ORE", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-3",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: false,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 42, 42)],
                AgentSymbol: null,
                AgentCredits: null,
                ShipCargo: new CargoModel(2, 15, [new CargoItemModel("IRON_ORE", 2)])));
        port.FulfillContractAsync("C-3", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-3",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: true,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 42, 42)],
                AgentSymbol: null,
                AgentCredits: 1000,
                ShipCargo: null));
        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<Wolverine.IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-3", "C-3", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await port.Received(1).DeliverContractAsync("C-3", "SHIP-3", "IRON_ORE", 3, Arg.Any<CancellationToken>());
        await port.Received(1).FulfillContractAsync("C-3", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RecordsTheContractPaymentInTheAgentsCredits()
    {
        // B33: the soak test's cached credits stayed 6,620 below the game's after fulfilment, so
        // every purchase was budgeted from too little until the next restart's sync.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var agents = Substitute.For<IAgentRepository>();
        ships.FindAsync("SHIP-4", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-4", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 80, CargoCapacity: 15));
        contracts.FindAsync("C-4", Arg.Any<CancellationToken>()).Returns(Contract("C-4", required: 42, fulfilled: 42));
        agents.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new AgentModel(Symbol: "AGENT", AccountId: null, HeadquartersSymbol: null, Credits: 130_564, StartingFaction: "COSMIC", ShipCount: 3));
        port.FulfillContractAsync("C-4", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult(
                ContractId: "C-4",
                FactionSymbol: "COSMIC",
                ContractType: "PROCUREMENT",
                IsAccepted: true,
                IsFulfilled: true,
                Expiration: DateTimeOffset.UtcNow.AddDays(3),
                DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
                Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", 42, 42)],
                AgentSymbol: "AGENT",
                AgentCredits: 137_184,
                ShipCargo: null));
        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<Wolverine.IMessageBus>(),
            agents,
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-4", "C-4", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await port.Received(1).FulfillContractAsync("C-4", Arg.Any<CancellationToken>());
        await agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(a => a.Credits == 137_184), Arg.Any<CancellationToken>());
    }

    private static ContractDto Contract(string id, int required, int fulfilled) =>
        new(
            Id: id,
            FactionSymbol: "COSMIC",
            Type: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            DeliverablesJson: JsonSerializer.Serialize(new List<ContractDeliverableDto> { new("IRON_ORE", "X1-AB-MKT", required, fulfilled) }));
}

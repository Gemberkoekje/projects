using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Events;
using Wolverine;
using static SpaceTraders.Application.Tests.Commands.FuelStopFixture;

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
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            bus,
            agents,
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
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
            Substitute.For<ITradeContextReader>(),
            dock,
            Substitute.For<IRefuelSubCommand>(),
            orbit,
            Substitute.For<IFlightModeSubCommand>(),
            navigate,
            bus,
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
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
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var tradeContexts = Substitute.For<ITradeContextReader>();
        tradeContexts.ReadAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns(new TradeContext(new TradeMarketMap([], [], new Dictionary<string, IReadOnlyList<string>>()), 0, 0));

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
            tradeContexts,
            dock,
            Substitute.For<IRefuelSubCommand>(),
            orbit,
            Substitute.For<IFlightModeSubCommand>(),
            navigate,
            bus,
            Substitute.For<IAgentRepository>(),
            assignments,
            Substitute.For<ITripBook>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        var result = await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-2", "C-2", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        result.Accepted.Should().BeTrue();
        await navigate.Received(1).ExecuteAsync("SHIP-2", "X1-AB-MKT", Guid.Empty, Arg.Any<CancellationToken>());
        await port.DidNotReceiveWithAnyArgs().DeliverContractAsync(default!, default!, default!, default, default);

        // On its way, the ship is still on its trip (D26).
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
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
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<Wolverine.IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
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
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<Wolverine.IMessageBus>(),
            agents,
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-4", "C-4", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await port.Received(1).FulfillContractAsync("C-4", Arg.Any<CancellationToken>());
        await agents.Received(1).UpsertAsync(Arg.Is<AgentModel>(a => a.Credits == 137_184), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotFulfilAContractAnotherShipFulfilled()
    {
        // D23: several ships deliver to one contract. One that arrives after another has fulfilled it
        // delivers nothing and leaves the fulfilment alone; the plan then releases it with its ore.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ship = new ShipModel("SHIP-4", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 80, CargoCurrent: 12, CargoCapacity: 15, CargoInventory: [new CargoItemModel("IRON_ORE", 12)]);
        ships.FindAsync("SHIP-4", Arg.Any<CancellationToken>()).Returns(ship);
        contracts.FindAsync("C-5", Arg.Any<CancellationToken>()).Returns(Contract("C-5", required: 42, fulfilled: 42) with { IsFulfilled = true });

        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-4", "C-5", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await port.DidNotReceiveWithAnyArgs().DeliverContractAsync(default!, default!, default!, default, default);
        await port.DidNotReceiveWithAnyArgs().FulfillContractAsync(default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_ADelivery_EndsTheShipsTrip()
    {
        // D26, seen on the cluster on 2026-10-02: the command ship joined the contract while the survey
        // plan was still off, and its assignment lasted until the contract was fulfilled, so it went on
        // mining after the survey plan was switched on (D20). Released at each delivery, a ship is
        // assigned again on the next tick by the plans in their order, so work that matters more comes
        // first.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var ship = new ShipModel("SHIP-6", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 80, CargoCurrent: 15, CargoCapacity: 15, CargoInventory: [new CargoItemModel("IRON_ORE", 15)]);
        ships.FindAsync("SHIP-6", Arg.Any<CancellationToken>()).Returns(ship);
        contracts.FindAsync("C-6", Arg.Any<CancellationToken>()).Returns(Contract("C-6", required: 42, fulfilled: 10), Contract("C-6", required: 42, fulfilled: 25));
        port.DeliverContractAsync("C-6", "SHIP-6", "IRON_ORE", 15, Arg.Any<CancellationToken>())
            .Returns(Delivered("C-6", required: 42, fulfilled: 25));
        assignments.FindAsync("SHIP-6", Arg.Any<CancellationToken>()).Returns(Assignment("SHIP-6", "C-6"));
        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            assignments,
            Substitute.For<ITripBook>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-6", "C-6", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await port.DidNotReceiveWithAnyArgs().FulfillContractAsync(default!, default);
        await assignments.Received(1).UpsertAsync(
            Arg.Is<ShipAssignmentDto>(a => a.ShipSymbol == "SHIP-6" && a.ContractId == "C-6" && a.CompletedAt.HasValue),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ADelivery_BooksTheRoundTrip_FromWhenTheShipWasAssigned()
    {
        // D46: contract work is booked as activity contract. Its round trip ends at the delivery, and costs the fuel the
        // ship bought since it was assigned; the contract's payments count as its profit when they come.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var trips = Substitute.For<ITripBook>();
        var assignment = Assignment("SHIP-6", "C-6");
        ships.FindAsync("SHIP-6", Arg.Any<CancellationToken>()).Returns(new ShipModel("SHIP-6", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 80, CargoCurrent: 15, CargoCapacity: 15, CargoInventory: [new CargoItemModel("IRON_ORE", 15)]));
        contracts.FindAsync("C-6", Arg.Any<CancellationToken>()).Returns(Contract("C-6", required: 42, fulfilled: 10), Contract("C-6", required: 42, fulfilled: 25));
        port.DeliverContractAsync("C-6", "SHIP-6", "IRON_ORE", 15, Arg.Any<CancellationToken>())
            .Returns(Delivered("C-6", required: 42, fulfilled: 25));
        assignments.FindAsync("SHIP-6", Arg.Any<CancellationToken>()).Returns(assignment);
        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            assignments,
            trips,
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        await sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-6", "C-6", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await trips.Received(1).BookContractTripAsync("SHIP-6", assignment.AssignedAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheFulfilmentFails_TheShipKeepsItsAssignment()
    {
        // D26: the plan gives no ship a trip once every unit is delivered, so a ship released with the
        // fulfilment still to make would leave it, and its payment, to nobody. Kept on its assignment,
        // the ship makes the call again on the next tick.
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var trips = Substitute.For<ITripBook>();
        var ship = new ShipModel("SHIP-7", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 80, CargoCurrent: 2, CargoCapacity: 15, CargoInventory: [new CargoItemModel("IRON_ORE", 2)]);
        ships.FindAsync("SHIP-7", Arg.Any<CancellationToken>()).Returns(ship);
        contracts.FindAsync("C-7", Arg.Any<CancellationToken>()).Returns(Contract("C-7", required: 42, fulfilled: 40), Contract("C-7", required: 42, fulfilled: 42));
        port.DeliverContractAsync("C-7", "SHIP-7", "IRON_ORE", 2, Arg.Any<CancellationToken>())
            .Returns(Delivered("C-7", required: 42, fulfilled: 42));
        port.FulfillContractAsync("C-7", Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException("The API timed out."));
        assignments.FindAsync("SHIP-7", Arg.Any<CancellationToken>()).Returns(Assignment("SHIP-7", "C-7"));
        var sut = new FulfillContractDeliveryHandler(
            port,
            ships,
            contracts,
            Substitute.For<ITradeContextReader>(),
            Substitute.For<IDockSubCommand>(),
            Substitute.For<IRefuelSubCommand>(),
            Substitute.For<IOrbitSubCommand>(),
            Substitute.For<IFlightModeSubCommand>(),
            Substitute.For<INavigateSubCommand>(),
            Substitute.For<IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            assignments,
            trips,
            NullLogger<FulfillContractDeliveryHandler>.Instance);

        var deliver = () => sut.ExecuteAsync(new FulfillContractDeliveryCommand("SHIP-7", "C-7", "IRON_ORE", "X1-AB-MKT"), CancellationToken.None);

        await deliver.Should().ThrowAsync<HttpRequestException>();
        await assignments.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);

        // Its round trip is booked when the call succeeds (D46).
        await trips.DidNotReceiveWithAnyArgs().BookContractTripAsync(default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_TheDeliveryAfterADrift_GoesInCruise()
    {
        // B47, on the cluster on 2026-10-04: the navigation's fallback drifted SPECTER-1 to the contract's asteroid, EF5D,
        // and left it in DRIFT, so it took the copper the 19 to H60 in DRIFT too: 148 seconds instead of about 30.
        var ship = new FlyingShip(CommandShip(EF5D, "IN_ORBIT", "DRIFT", fuel: 399, cargo: [new CargoItemModel("COPPER_ORE", 40)]));

        await Handler(ship, Substitute.For<ISpaceTradersPort>())
            .ExecuteAsync(new FulfillContractDeliveryCommand("SPECTER-1", "C-1", "COPPER_ORE", H60), CancellationToken.None);

        ship.Flights.Should().Equal(new Flight(H60, "CRUISE", 399, 19));
    }

    [Fact]
    public async Task ExecuteAsync_ADeliveryBeyondOneTank_GoesInCruise_RefuellingOnTheWay()
    {
        // B47: a delivery from beyond one tank refuels on the way, as the flight to the asteroid does, rather than drifting.
        // With the copper aboard at J67, H60 is 765 away. Each tick flies a leg, and the next one dead-reckons its arrival.
        var port = Substitute.For<ISpaceTradersPort>();
        port.DeliverContractAsync("C-1", "SPECTER-1", "COPPER_ORE", 40, Arg.Any<CancellationToken>())
            .Returns(Delivered("C-1", required: 120, fulfilled: 80));
        var ship = new FlyingShip(CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 40)]));

        for (var tick = 0; tick < 4; tick++)
        {
            await Handler(ship, port)
                .ExecuteAsync(new FulfillContractDeliveryCommand("SPECTER-1", "C-1", "COPPER_ORE", H60), CancellationToken.None);
        }

        ship.Flights.Should().Equal(
            new Flight(J66, "CRUISE", 400, 119),
            new Flight(I65, "CRUISE", 400, 372),
            new Flight(H60, "CRUISE", 400, 274));
        await port.Received(1).DeliverContractAsync("C-1", "SPECTER-1", "COPPER_ORE", 40, Arg.Any<CancellationToken>());
    }

    /// <summary>A handler for a ship in <see cref="FuelStopFixture"/>, whose contract's terms aren't cached.</summary>
    private static FulfillContractDeliveryHandler Handler(FlyingShip ship, ISpaceTradersPort port)
    {
        var tradeContexts = Substitute.For<ITradeContextReader>();
        tradeContexts.ReadAsync(SystemSymbol, Arg.Any<CancellationToken>()).Returns(Context());
        return new FulfillContractDeliveryHandler(
            port,
            ship.Ships,
            Substitute.For<IContractRepository>(),
            tradeContexts,
            ship.Dock,
            ship.Refuel,
            ship.Orbit,
            ship.FlightMode,
            ship.Navigate,
            Substitute.For<IMessageBus>(),
            Substitute.For<IAgentRepository>(),
            Substitute.For<IShipAssignmentRepository>(),
            Substitute.For<ITripBook>(),
            NullLogger<FulfillContractDeliveryHandler>.Instance);
    }

    private static ShipAssignmentDto Assignment(string ship, string contractId)
        => new(ship, "Contract", "X1-AB-AST", "X1-AB-MKT", "IRON_ORE", contractId, 0, DateTimeOffset.UtcNow.AddMinutes(-20), null, RequiredUnits: 32);

    private static ContractActionResult Delivered(string id, int required, int fulfilled) =>
        new(
            ContractId: id,
            FactionSymbol: "COSMIC",
            ContractType: "PROCUREMENT",
            IsAccepted: true,
            IsFulfilled: false,
            Expiration: DateTimeOffset.UtcNow.AddDays(3),
            DeadlineToAccept: DateTimeOffset.UtcNow.AddHours(1),
            TermsDeadline: DateTimeOffset.UtcNow.AddDays(1),
            Deliverables: [new ContractDeliverableModel("IRON_ORE", "X1-AB-MKT", required, fulfilled)],
            AgentSymbol: null,
            AgentCredits: null,
            ShipCargo: new CargoModel(0, 15, []));

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

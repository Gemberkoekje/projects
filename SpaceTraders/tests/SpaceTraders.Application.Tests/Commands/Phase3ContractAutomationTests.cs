using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class Phase3ContractAutomationTests
{
    [Fact]
    public async Task DeliverContract_UsesPendingUnitsForPartialDelivery()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        var deliverablesJson = JsonSerializer.Serialize(new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-DEL", 40, 35),
        });

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", true, false, DateTimeOffset.UtcNow.AddHours(4), null, null, deliverablesJson));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-DEL", "DOCKED", "CRUISE", 100, 100, CargoCurrent: 10, CargoCapacity: 40,
                CargoInventory: [new CargoItemModel("IRON_ORE", 10)]));

        port.DeliverContractAsync("CONTRACT-1", "SHIP-1", "IRON_ORE", 5, Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult("CONTRACT-1", true, false, null, null, new CargoModel(5, 40, [new CargoItemModel("IRON_ORE", 5)])));

        var handler = new DeliverContractHandler(port, contracts, ships, bus, NullLogger<DeliverContractHandler>.Instance);
        await handler.Handle(new DeliverContractCommand("CONTRACT-1", "SHIP-1", "IRON_ORE", 10, "X1-AB-DEL"), CancellationToken.None);

        await port.Received(1).DeliverContractAsync("CONTRACT-1", "SHIP-1", "IRON_ORE", 5, Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Any<ContractDeliveryRecordedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task AcceptContract_SkipsWhenExpiredDeadlineToAccept()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", false, false, null, DateTimeOffset.UtcNow.AddMinutes(-1)));

        var handler = new AcceptContractHandler(port, contracts, agents, bus, NullLogger<AcceptContractHandler>.Instance);
        await handler.Handle(new AcceptContractCommand("CONTRACT-1"), CancellationToken.None);

        await port.DidNotReceive().AcceptContractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContractObjectivePlanner_BuildsContractAssignmentFromDeliverable()
    {
        var contracts = Substitute.For<IContractRepository>();
        var markets = Substitute.For<IMarketRepository>();

        var deliverablesJson = JsonSerializer.Serialize(new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-DEL", 20, 5),
        });

        contracts.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns([
                new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", true, false, DateTimeOffset.UtcNow.AddHours(6), null, DateTimeOffset.UtcNow.AddHours(3), deliverablesJson)
            ]);

        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new MarketSnapshot(
                    "X1-AB-BUY",
                    "X1-AB",
                    [],
                    [],
                    ["IRON_ORE"],
                    [])
            ]);

        var ship = new ShipModel("SHIP-1", "X1-AB", "X1-AB-HQ", "DOCKED", "CRUISE", 100, 100, CargoCurrent: 0, CargoCapacity: 40);

        var planner = new ContractObjectivePlanner(contracts, markets);
        var plan = await planner.PlanAsync(ship, CancellationToken.None);

        plan.Should().NotBeNull();
        plan!.AssignmentType.Should().Be("Contract");
        plan.ContractId.Should().Be("CONTRACT-1");
        plan.CargoSymbol.Should().Be("IRON_ORE");
        plan.OriginWaypoint.Should().Be("X1-AB-BUY");
        plan.DestWaypoint.Should().Be("X1-AB-DEL");
        plan.RequiredUnits.Should().Be(15);
    }
}

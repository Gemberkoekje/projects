using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class ContractMutationHandlerTests
{
    [Fact]
    public async Task AcceptContract_PublishesAcceptedEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns((ContractDto)null!);

        port.AcceptContractAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult("CONTRACT-1", true, false, "AGENT-1", 48_000, null));

        var handler = new AcceptContractHandler(port, contracts, agents, bus, NullLogger<AcceptContractHandler>.Instance);
        await handler.Handle(new AcceptContractCommand("CONTRACT-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ContractAcceptedEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task FulfillContract_PublishesFulfilledEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        var deliverablesJson = JsonSerializer.Serialize(new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-1", 10, 10),
        });

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", true, false, null, null, null, deliverablesJson));

        port.FulfillContractAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractActionResult("CONTRACT-1", true, true, "AGENT-1", 60_000, null));

        var handler = new FulfillContractHandler(port, contracts, agents, bus, NullLogger<FulfillContractHandler>.Instance);
        await handler.Handle(new FulfillContractCommand("CONTRACT-1"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ContractFulfilledEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DeliverContract_WhenShipNotDocked_PublishesMismatch()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = Substitute.For<IShipRepository>();
        var bus = Substitute.For<IMessageBus>();

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", true, false, null, null));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 100, 100));

        var handler = new DeliverContractHandler(port, contracts, ships, bus, NullLogger<DeliverContractHandler>.Instance);
        await handler.Handle(new DeliverContractCommand("CONTRACT-1", "SHIP-1", "IRON_ORE", 10, "X1-AB-001"), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<ShipStateMismatchEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task FulfillContract_SkipsWhenDeliverablesArePending()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var agents = Substitute.For<IAgentRepository>();
        var bus = Substitute.For<IMessageBus>();

        var deliverablesJson = JsonSerializer.Serialize(new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-1", 10, 9),
        });

        contracts.FindAsync("CONTRACT-1", Arg.Any<CancellationToken>())
            .Returns(new ContractDto("CONTRACT-1", "COSMIC", "PROCUREMENT", true, false, null, null, null, deliverablesJson));

        var handler = new FulfillContractHandler(port, contracts, agents, bus, NullLogger<FulfillContractHandler>.Instance);
        await handler.Handle(new FulfillContractCommand("CONTRACT-1"), CancellationToken.None);

        await port.DidNotReceive().FulfillContractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ContractFulfilledEvent>(), Arg.Any<DeliveryOptions>());
    }
}

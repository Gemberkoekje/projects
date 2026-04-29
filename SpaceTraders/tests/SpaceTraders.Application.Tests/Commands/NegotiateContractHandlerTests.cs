using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class NegotiateContractHandlerTests
{
    [Fact]
    public async Task NegotiateContract_WhenNotDocked_DoesNotCallPort()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "IN_ORBIT", "CRUISE", 80, 100));

        var handler = new NegotiateContractHandler(port, ships, contracts, bus, NullLogger<NegotiateContractHandler>.Instance);
        await handler.Handle(new NegotiateContractCommand("SHIP-1"), CancellationToken.None);

        await port.DidNotReceive().NegotiateContractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NegotiateContract_WhenDocked_CallsPort_AndUpsertsContract()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var ships = Substitute.For<IShipRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var bus = Substitute.For<IMessageBus>();
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 80, 100));
        var contract = new ContractModel("CTR-1", "COSMIC", "PROCUREMENT", false, false, null, null);
        port.NegotiateContractAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new NegotiateContractActionResult(contract));

        var handler = new NegotiateContractHandler(port, ships, contracts, bus, NullLogger<NegotiateContractHandler>.Instance);
        await handler.Handle(new NegotiateContractCommand("SHIP-1"), CancellationToken.None);

        await port.Received(1).NegotiateContractAsync("SHIP-1", Arg.Any<CancellationToken>());
        await contracts.Received(1).UpsertAsync(
            Arg.Is<ContractDto>(d => d.Id == "CTR-1"),
            Arg.Any<CancellationToken>());
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class DeliverContractHandlerTests
{
    [Fact]
    public async Task DeliverContract_WhenNotDocked_PublishesStateMismatch_AndSkipsApi()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var contracts = Substitute.For<IContractRepository>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            SystemSymbol = "X1-AB",
            WaypointSymbol = "X1-AB-001",
            Status = "IN_ORBIT",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new DeliverContractHandler(port, contracts, ships, bus, NullLogger<DeliverContractHandler>.Instance)
            .Handle(new DeliverContractCommand("CONTRACT-1", "SHIP-1", "IRON_ORE", 10, "X1-AB-001"), CancellationToken.None);

        await port.DidNotReceive().DeliverContractAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipStateMismatchEvent>(e =>
                e.ShipSymbol == "SHIP-1" &&
                e.CommandName == nameof(DeliverContractCommand) &&
                e.RequiredState == "DOCKED_AT_CONTRACT_DESTINATION" &&
                e.ActualState == "IN_ORBIT"),
            Arg.Any<DeliveryOptions>());
    }
}

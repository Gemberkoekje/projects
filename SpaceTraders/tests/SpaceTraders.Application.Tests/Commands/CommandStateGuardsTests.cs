using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class CommandStateGuardsTests
{
    [Fact]
    public async Task OrbitShip_WhenNotDocked_PublishesMismatch_AndSkipsApi()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            Status = "IN_ORBIT",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new OrbitShipHandler(port, ships, bus, NullLogger<OrbitShipHandler>.Instance)
            .Handle(new OrbitShipCommand("SHIP-1"), CancellationToken.None);

        await port.DidNotReceive().OrbitShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipStateMismatchEvent>(e =>
                e.CommandName == nameof(OrbitShipCommand) &&
                e.RequiredState == "DOCKED" &&
                e.ActualState == "IN_ORBIT"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task DockShip_WhenNotInOrbit_PublishesMismatch_AndSkipsApi()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            AgentToken = TestDbContextFactory.AgentToken,
            Symbol = "SHIP-1",
            Status = "DOCKED",
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);

        await new DockShipHandler(port, ships, bus, NullLogger<DockShipHandler>.Instance)
            .Handle(new DockShipCommand("SHIP-1"), CancellationToken.None);

        await port.DidNotReceive().DockShipAsync("SHIP-1", Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ShipStateMismatchEvent>(e =>
                e.CommandName == nameof(DockShipCommand) &&
                e.RequiredState == "IN_ORBIT" &&
                e.ActualState == "DOCKED"),
            Arg.Any<DeliveryOptions>());
    }
}

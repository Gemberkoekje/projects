using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class SellCargoHandlerTests
{
    [Fact]
    public async Task SellCargo_AppliesCargoAndCreditsFromResponse()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", CargoCurrent = 10, CargoCapacity = 60, LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);

        await new SellCargoHandler(port, ships, agents, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.CargoCurrent.Should().Be(0);

        var agent = await db.Agents.FindAsync("AGENT-1");
        agent!.Credits.Should().Be(55_000);
    }

    [Fact]
    public async Task SellCargo_PublishesShipCargoSoldEvent()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);

        await new SellCargoHandler(port, ships, agents, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_DoesNotIssueFollowUpGets()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
            .Returns(new TradeActionResult("AGENT-1", 55_000, new CargoModel(0, 60), 5_000));

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var ships = new ShipRepository(db);
        var agents = new AgentRepository(db);

        await new SellCargoHandler(port, ships, agents, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await port.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}

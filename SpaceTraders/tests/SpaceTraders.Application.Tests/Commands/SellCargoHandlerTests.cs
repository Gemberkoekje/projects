using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class SellCargoHandlerTests
{
    private static MarketTransaction MakeTx(string tradeSymbol = "IRON_ORE", int units = 10, long total = 5_000) =>
        new()
        {
            WaypointSymbol = "X1-AB-001", ShipSymbol = "SHIP-1",
            TradeSymbol = tradeSymbol, Type = "SELL",
            Units = units, PricePerUnit = total / units, TotalPrice = total,
            Timestamp = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task SellCargo_AppliesCargoAndCreditsFromResponse()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
           .Returns(new SellCargoResult
           {
               Agent = new Agent { Symbol = "AGENT-1", Credits = 55_000, StartingFaction = "COSMIC", ShipCount = 1 },
               Cargo = new ShipCargo { Capacity = 60, Units = 0 },
               Transaction = MakeTx()
           });

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", CargoCurrent = 10, CargoCapacity = 60, LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new SellCargoHandler(api, db, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.CargoCurrent.Should().Be(0);

        var agent = await db.Agents.FindAsync("AGENT-1");
        agent!.Credits.Should().Be(55_000);
    }

    [Fact]
    public async Task SellCargo_PublishesShipCargoSoldEvent()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
           .Returns(new SellCargoResult
           {
               Agent = new Agent { Symbol = "AGENT-1", Credits = 55_000, StartingFaction = "COSMIC", ShipCount = 1 },
               Cargo = new ShipCargo { Capacity = 60, Units = 0 },
               Transaction = MakeTx(total: 5_000)
           });

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new SellCargoHandler(api, db, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Any<object>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task SellCargo_DoesNotIssueFollowUpGets()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>())
           .Returns(new SellCargoResult
           {
               Agent = new Agent { Symbol = "AGENT-1", Credits = 55_000, StartingFaction = "COSMIC", ShipCount = 1 },
               Cargo = new ShipCargo { Capacity = 60, Units = 0 },
               Transaction = MakeTx()
           });

        var bus = Substitute.For<IMessageBus>();

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        db.Agents.Add(new CachedAgent { Symbol = "AGENT-1", StartingFaction = "COSMIC", Credits = 50_000, ShipCount = 1, LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new SellCargoHandler(api, db, bus, NullLogger<SellCargoHandler>.Instance)
            .Handle(new SellCargoCommand("SHIP-1", "IRON_ORE", 10), CancellationToken.None);

        await api.Received(1).SellCargoAsync("SHIP-1", "IRON_ORE", 10, Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
    }
}

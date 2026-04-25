using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncAllShipsHandlerTests
{
    private static ShipModel MakeShip(string symbol, string status = "DOCKED", DateTimeOffset? arrival = null) =>
        new(symbol, "X1-AB", "X1-AB-001", status, "CRUISE", 80, 100, arrival);

    private static PagedResult<ShipModel> OnePage(params ShipModel[] ships) =>
        new(ships, ships.Length, 1, 20);

    [Fact]
    public async Task Handle_PersistsAllShips()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeShip("SHIP-1"), MakeShip("SHIP-2")));

        await using var db = TestDbContextFactory.Create();
        var ships = new ShipRepository(db);

        await new SyncAllShipsHandler(port, ships, NullLogger<SyncAllShipsHandler>.Instance)
            .Handle(new SyncAllShipsCommand(), CancellationToken.None);

        db.Ships.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_InTransitShip_PersistsArrivesAt()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeShip("SHIP-1", status: "IN_TRANSIT", arrival: arrival)));

        await using var db = TestDbContextFactory.Create();
        var ships = new ShipRepository(db);

        await new SyncAllShipsHandler(port, ships, NullLogger<SyncAllShipsHandler>.Instance)
            .Handle(new SyncAllShipsCommand(), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.ArrivesAt.Should().BeCloseTo(arrival, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_DoesNotIssueAnyNonShipGetCalls()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeShip("SHIP-1")));

        await using var db = TestDbContextFactory.Create();
        var ships = new ShipRepository(db);

        await new SyncAllShipsHandler(port, ships, NullLogger<SyncAllShipsHandler>.Instance)
            .Handle(new SyncAllShipsCommand(), CancellationToken.None);

        await port.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await port.DidNotReceive().GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaginatesUntilAllShipsFetched()
    {
        var port = Substitute.For<ISpaceTradersPort>();

        port.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(new PagedResult<ShipModel>([MakeShip("SHIP-1"), MakeShip("SHIP-2")], 3, 1, 20));

        port.GetMyShipsAsync(2, 20, Arg.Any<CancellationToken>())
            .Returns(OnePage(MakeShip("SHIP-3")));

        await using var db = TestDbContextFactory.Create();
        var ships = new ShipRepository(db);

        await new SyncAllShipsHandler(port, ships, NullLogger<SyncAllShipsHandler>.Instance)
            .Handle(new SyncAllShipsCommand(), CancellationToken.None);

        db.Ships.Should().HaveCount(3);
    }
}

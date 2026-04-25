using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Sync;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Sync;

public sealed class SyncAllShipsHandlerTests
{
    private static Ship MakeShip(string symbol, string status = "DOCKED", DateTimeOffset? arrival = null) =>
        new()
        {
            Symbol = symbol,
            Nav = new ShipNav
            {
                SystemSymbol = "X1-AB",
                WaypointSymbol = "X1-AB-001",
                Status = status,
                FlightMode = "CRUISE",
                Route = arrival.HasValue ? new ShipNavRoute { Arrival = arrival.Value } : null
            },
            Fuel = new ShipFuel { Current = 80, Capacity = 100 }
        };

    private static PagedApiResponse<Ship> OnePage(params Ship[] ships) =>
        new() { Data = ships, Meta = new Meta { Total = ships.Length, Page = 1, Limit = 20 } };

    [Fact]
    public async Task Handle_PersistsAllShips()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeShip("SHIP-1"), MakeShip("SHIP-2")));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAllShipsHandler(apiClient, db, NullLogger<SyncAllShipsHandler>.Instance);

        await handler.Handle(new SyncAllShipsCommand(), CancellationToken.None);

        db.Ships.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_InTransitShip_PersistsArrivesAt()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeShip("SHIP-1", status: "IN_TRANSIT", arrival: arrival)));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAllShipsHandler(apiClient, db, NullLogger<SyncAllShipsHandler>.Instance);

        await handler.Handle(new SyncAllShipsCommand(), CancellationToken.None);

        var ship = await db.Ships.FindAsync("SHIP-1");
        ship!.ArrivesAt.Should().BeCloseTo(arrival, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_DoesNotIssueAnyNonShipGetCalls()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyShipsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeShip("SHIP-1")));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAllShipsHandler(apiClient, db, NullLogger<SyncAllShipsHandler>.Instance);

        await handler.Handle(new SyncAllShipsCommand(), CancellationToken.None);

        await apiClient.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
        await apiClient.DidNotReceive().GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaginatesUntilAllShipsFetched()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();

        // Page 1: 2 ships out of 3 total
        apiClient.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
                 .Returns(new PagedApiResponse<Ship>
                 {
                     Data = [MakeShip("SHIP-1"), MakeShip("SHIP-2")],
                     Meta = new Meta { Total = 3, Page = 1, Limit = 20 }
                 });

        // Page 2: 1 ship
        apiClient.GetMyShipsAsync(2, 20, Arg.Any<CancellationToken>())
                 .Returns(OnePage(MakeShip("SHIP-3")));

        await using var db = TestDbContextFactory.Create();
        var handler = new SyncAllShipsHandler(apiClient, db, NullLogger<SyncAllShipsHandler>.Instance);

        await handler.Handle(new SyncAllShipsCommand(), CancellationToken.None);

        db.Ships.Should().HaveCount(3);
    }
}

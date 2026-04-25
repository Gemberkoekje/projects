using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class NavigateShipHandlerTests
{
    private static ShipNav MakeNav(string waypoint, string destWaypoint, DateTimeOffset? arrival = null, string status = "IN_TRANSIT") =>
        new()
        {
            SystemSymbol = "X1-AB",
            WaypointSymbol = waypoint,
            Status = status,
            FlightMode = "CRUISE",
            Route = arrival.HasValue
                ? new ShipNavRoute
                {
                    Arrival = arrival.Value,
                    DepartureTime = DateTimeOffset.UtcNow,
                    Destination = new ShipNavRouteWaypoint { Symbol = destWaypoint, SystemSymbol = "X1-AB" }
                }
                : null
        };

    [Fact]
    public async Task NavigateShip_UpdatesNavAndArrivesAt_FromResponse()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(3);
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
           .Returns(new NavigateResult
           {
               Nav = MakeNav("X1-AB-001", "X1-AB-002", arrival),
               Fuel = new ShipFuel { Current = 70, Capacity = 100 }
           });

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip
        {
            Symbol = "SHIP-1",
            SystemSymbol = "X1-AB",
            WaypointSymbol = "X1-AB-001",
            Status = "IN_ORBIT",
            FlightMode = "CRUISE",
            FuelCurrent = 100,
            FuelCapacity = 100,
            LastSyncedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await new NavigateShipHandler(api, db, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        var cached = await db.Ships.FindAsync("SHIP-1");
        cached!.Status.Should().Be("IN_TRANSIT");
        cached.ArrivesAt.Should().BeCloseTo(arrival, TimeSpan.FromSeconds(1));
        cached.DestWaypointSymbol.Should().Be("X1-AB-002");
        cached.FuelCurrent.Should().Be(70);
    }

    [Fact]
    public async Task NavigateShip_DoesNotIssueFollowUpGets()
    {
        var api = Substitute.For<ISpaceTradersApiClient>();
        api.NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>())
           .Returns(new NavigateResult
           {
               Nav = MakeNav("X1-AB-001", "X1-AB-002", DateTimeOffset.UtcNow.AddMinutes(3))
           });

        await using var db = TestDbContextFactory.Create();
        db.Ships.Add(new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await new NavigateShipHandler(api, db, NullLogger<NavigateShipHandler>.Instance)
            .Handle(new NavigateShipCommand("SHIP-1", "X1-AB-002"), CancellationToken.None);

        await api.Received(1).NavigateShipAsync("SHIP-1", "X1-AB-002", Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyShipAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await api.DidNotReceive().GetMyAgentAsync(Arg.Any<CancellationToken>());
    }
}

using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Infrastructure.Tests;

[Trait("Category", "Integration")]
public sealed class ShipRepositoryTests : IntegrationTestBase
{
    [SkippableFact]
    public async Task UpsertAsync_NewShip_CanBeFoundAfterwards()
    {
        var repo = new ShipRepository(Db);
        var ship = new ShipModel(
            Symbol: "SHIP-1",
            SystemSymbol: "X1-SYSTEM",
            WaypointSymbol: "X1-WP1",
            Status: "IN_ORBIT",
            FlightMode: "CRUISE",
            FuelCurrent: 400,
            FuelCapacity: 400,
            ArrivesAt: null,
            DestWaypointSymbol: null,
            CargoCurrent: 0,
            CargoCapacity: 60);

        await repo.UpsertAsync(ship);

        await using var fresh = CreateFreshContext();
        var freshRepo = new ShipRepository(fresh);
        var result = await freshRepo.FindAsync("SHIP-1");

        result.Should().NotBeNull();
        result!.Symbol.Should().Be("SHIP-1");
        result.SystemSymbol.Should().Be("X1-SYSTEM");
        result.WaypointSymbol.Should().Be("X1-WP1");
        result.Status.Should().Be("IN_ORBIT");
        result.FuelCurrent.Should().Be(400);
        result.FuelCapacity.Should().Be(400);
        result.CargoCapacity.Should().Be(60);
    }

    [SkippableFact]
    public async Task UpsertAsync_ExistingShip_UpdatesFields()
    {
        var repo = new ShipRepository(Db);
        await repo.UpsertAsync(new ShipModel("SHIP-2", "S1", "WP1", "DOCKED", "CRUISE", 300, 400, null));

        await repo.UpsertAsync(new ShipModel("SHIP-2", "S1", "WP2", "IN_ORBIT", "BURN", 200, 400, null));

        await using var fresh = CreateFreshContext();
        var result = await new ShipRepository(fresh).FindAsync("SHIP-2");

        result!.WaypointSymbol.Should().Be("WP2");
        result.Status.Should().Be("IN_ORBIT");
        result.FlightMode.Should().Be("BURN");
        result.FuelCurrent.Should().Be(200);
    }

    [SkippableFact]
    public async Task UpdateNavAsync_SetsArrivesAt_MarksShipInTransit()
    {
        var repo = new ShipRepository(Db);
        await repo.UpsertAsync(new ShipModel("SHIP-3", "S1", "WP1", "IN_TRANSIT", "CRUISE", 300, 400, null));

        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        var nav = new NavModel(
            Status: "IN_TRANSIT",
            SystemSymbol: "S1",
            WaypointSymbol: "WP1",
            FlightMode: "CRUISE",
            DestWaypointSymbol: "WP2",
            ArrivesAt: arrival);

        await repo.UpdateNavAsync("SHIP-3", nav, new FuelModel(250, 400));

        await using var fresh = CreateFreshContext();
        var result = await new ShipRepository(fresh).FindAsync("SHIP-3");

        result!.ArrivesAt.Should().BeCloseTo(arrival, TimeSpan.FromSeconds(1));
        result.DestWaypointSymbol.Should().Be("WP2");
        result.FuelCurrent.Should().Be(250);
    }

    [SkippableFact]
    public async Task GetAllAsync_AppliesArrivalIfDue_ForArrivedShips()
    {
        var repo = new ShipRepository(Db);
        // Ship whose arrival time is in the past
        var pastArrival = DateTimeOffset.UtcNow.AddMinutes(-1);
        await repo.UpsertAsync(new ShipModel(
            "SHIP-4", "S1", "WP1", "IN_TRANSIT", "CRUISE", 300, 400,
            ArrivesAt: pastArrival,
            DestWaypointSymbol: "WP2"));

        await using var fresh = CreateFreshContext();
        var ships = await new ShipRepository(fresh).GetAllAsync();

        var ship = ships.Should().ContainSingle(s => s.Symbol == "SHIP-4").Subject;
        // dead-reckoning: waypoint advanced to destination, no longer in transit
        ship.WaypointSymbol.Should().Be("WP2");
        ship.IsInTransit.Should().BeFalse();
    }

    [SkippableFact]
    public async Task GetInTransitAsync_ReturnsOnlyShipsWithFutureArrival()
    {
        var repo = new ShipRepository(Db);
        var future = DateTimeOffset.UtcNow.AddMinutes(10);
        await repo.UpsertAsync(new ShipModel("SHIP-5", "S1", "WP1", "IN_TRANSIT", "CRUISE", 300, 400, future, "WP2"));
        await repo.UpsertAsync(new ShipModel("SHIP-6", "S1", "WP3", "IN_ORBIT", "CRUISE", 300, 400, null));

        await using var fresh = CreateFreshContext();
        var inTransit = await new ShipRepository(fresh).GetInTransitAsync();

        inTransit.Should().ContainSingle(s => s.Symbol == "SHIP-5");
        inTransit.Should().NotContain(s => s.Symbol == "SHIP-6");
    }

    [SkippableFact]
    public async Task GetAllAsync_IgnoresShipsFromOtherAgentTokens()
    {
        var repo = new ShipRepository(Db);
        await repo.UpsertAsync(new ShipModel(
            Symbol: "SHIP-OWN",
            SystemSymbol: "X1-SYS",
            WaypointSymbol: "X1-SYS-A1",
            Status: "DOCKED",
            FlightMode: "CRUISE",
            FuelCurrent: 100,
            FuelCapacity: 100,
            CargoCurrent: 0,
            CargoCapacity: 10));

        Db.Ships.Add(new CachedShip
        {
            AgentToken = "other-agent-token",
            Symbol = "SHIP-OTHER",
            SystemSymbol = "X1-OTHER",
            WaypointSymbol = "X1-OTHER-A1",
            Status = "DOCKED",
            FlightMode = "CRUISE",
            FuelCurrent = 100,
            FuelCapacity = 100,
            CargoCurrent = 0,
            CargoCapacity = 10,
            LastSyncedAt = DateTimeOffset.UtcNow,
        });
        await Db.SaveChangesAsync();

        await using var fresh = CreateFreshContext();
        var ships = await new ShipRepository(fresh).GetAllAsync();

        ships.Should().ContainSingle(s => s.Symbol == "SHIP-OWN");
        ships.Should().NotContain(s => s.Symbol == "SHIP-OTHER");
    }
}

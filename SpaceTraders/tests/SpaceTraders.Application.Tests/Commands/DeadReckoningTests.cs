using FluentAssertions;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Application.Tests.Commands;

public sealed class DeadReckoningTests
{
    [Fact]
    public void ApplyArrivalIfDue_WhenArrived_UpdatesWaypointAndClearsTransit()
    {
        var ship = new CachedShip
        {
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            DestWaypointSymbol = "X1-AB-002",
            Status = "IN_TRANSIT",
            ArrivesAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            LastSyncedAt = DateTimeOffset.UtcNow
        };

        ship.ApplyArrivalIfDue();

        ship.WaypointSymbol.Should().Be("X1-AB-002");
        ship.DestWaypointSymbol.Should().BeNull();
        ship.ArrivesAt.Should().BeNull();
        ship.Status.Should().Be("IN_ORBIT");
    }

    [Fact]
    public void ApplyArrivalIfDue_WhenStillInTransit_DoesNotChangeAnything()
    {
        var arrival = DateTimeOffset.UtcNow.AddMinutes(5);
        var ship = new CachedShip
        {
            Symbol = "SHIP-1",
            WaypointSymbol = "X1-AB-001",
            DestWaypointSymbol = "X1-AB-002",
            Status = "IN_TRANSIT",
            ArrivesAt = arrival,
            LastSyncedAt = DateTimeOffset.UtcNow
        };

        ship.ApplyArrivalIfDue();

        ship.WaypointSymbol.Should().Be("X1-AB-001");
        ship.DestWaypointSymbol.Should().Be("X1-AB-002");
        ship.ArrivesAt.Should().Be(arrival);
        ship.Status.Should().Be("IN_TRANSIT");
    }

    [Fact]
    public void IsInTransit_ReturnsFalse_WhenArrivesAtIsNull()
    {
        var ship = new CachedShip { Symbol = "SHIP-1", LastSyncedAt = DateTimeOffset.UtcNow };
        ship.IsInTransit.Should().BeFalse();
    }

    [Fact]
    public void IsInTransit_ReturnsFalse_WhenArrivesAtIsInThePast()
    {
        var ship = new CachedShip
        {
            Symbol = "SHIP-1",
            ArrivesAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            LastSyncedAt = DateTimeOffset.UtcNow
        };
        ship.IsInTransit.Should().BeFalse();
    }

    [Fact]
    public void IsInTransit_ReturnsTrue_WhenArrivesAtIsInTheFuture()
    {
        var ship = new CachedShip
        {
            Symbol = "SHIP-1",
            ArrivesAt = DateTimeOffset.UtcNow.AddMinutes(2),
            LastSyncedAt = DateTimeOffset.UtcNow
        };
        ship.IsInTransit.Should().BeTrue();
    }
}

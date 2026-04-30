using FluentAssertions;
using SpaceTraders.Domain.Aggregates;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Tests.Aggregates;

public sealed class ShipTests
{
    private static Ship CreateShip(
        ShipLocalStatus status = ShipLocalStatus.Docked,
        int fuelCurrent = 80,
        int fuelCapacity = 100) =>
        Ship.Reconstitute(
            symbol: "TEST-1",
            role: ShipRole.Command,
            status: status,
            flightMode: FlightMode.Cruise,
            currentWaypoint: new WaypointSymbol("X1-AB-001"),
            currentSystem: new SystemSymbol("X1-AB"),
            fuel: new Fuel(fuelCurrent, fuelCapacity),
            cargo: new Cargo(0, 40, []));

    [Fact]
    public void UpdateFuel_BelowThreshold_RaisesShipFuelLowEvent()
    {
        var ship = CreateShip(fuelCurrent: 80, fuelCapacity: 100);

        ship.UpdateFuel(new Fuel(5, 100));

        ship.DomainEvents.Should().ContainSingle(e => e is ShipFuelLowEvent);
        var evt = (ShipFuelLowEvent)ship.DomainEvents[0];
        evt.ShipSymbol.Should().Be("TEST-1");
        evt.CurrentFuel.Should().Be(5);
        evt.Capacity.Should().Be(100);
    }

    [Fact]
    public void UpdateFuel_AboveThreshold_DoesNotRaiseEvent()
    {
        var ship = CreateShip(fuelCurrent: 80, fuelCapacity: 100);

        ship.UpdateFuel(new Fuel(50, 100));

        ship.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateFuel_AtExactlyThreshold_DoesNotRaiseEvent()
    {
        var ship = CreateShip();

        ship.UpdateFuel(new Fuel(20, 100)); // exactly 20 % – not below

        ship.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void UpdateNav_FromInTransit_RaisesShipArrivedAtWaypointEvent()
    {
        var ship = CreateShip(status: ShipLocalStatus.InTransit);

        ship.UpdateNav(
            ShipLocalStatus.Docked,
            new WaypointSymbol("X1-AB-002"),
            new SystemSymbol("X1-AB"),
            arrivesAt: null);

        ship.DomainEvents.Should().ContainSingle(e => e is ShipArrivedAtWaypointEvent);
        var evt = (ShipArrivedAtWaypointEvent)ship.DomainEvents[0];
        evt.Waypoint.Value.Should().Be("X1-AB-002");
    }

    [Fact]
    public void UpdateNav_NotFromInTransit_DoesNotRaiseArrivedEvent()
    {
        var ship = CreateShip(status: ShipLocalStatus.InOrbit);

        ship.UpdateNav(
            ShipLocalStatus.Docked,
            new WaypointSymbol("X1-AB-002"),
            new SystemSymbol("X1-AB"),
            arrivesAt: null);

        ship.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void CompleteAssignment_WithAssignment_RaisesCompletedEvent()
    {
        var ship = CreateShip();
        ship.Assign(new SpaceTraders.Domain.Aggregates.ShipAssignment(
            AssignmentType.Trade,
            Origin: null,
            Destination: null,
            Cargo: null,
            ContractId: null,
            AssignedAt: DateTimeOffset.UtcNow));

        ship.CompleteAssignment();

        ship.DomainEvents.Should().ContainSingle(e => e is ShipAssignmentCompletedEvent);
        ship.Assignment.Should().BeNull();
    }

    [Fact]
    public void CompleteAssignment_WithNoAssignment_DoesNotRaiseEvent()
    {
        var ship = CreateShip();

        ship.CompleteAssignment();

        ship.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var ship = CreateShip();
        ship.UpdateFuel(new Fuel(5, 100));
        ship.DomainEvents.Should().NotBeEmpty();

        ship.ClearDomainEvents();

        ship.DomainEvents.Should().BeEmpty();
    }
}

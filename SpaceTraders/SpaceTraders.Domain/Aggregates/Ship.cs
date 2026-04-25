using SpaceTraders.Domain.Common;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Aggregates;

public sealed class Ship : AggregateRoot
{
    private const double FuelLowThreshold = 0.20;

    public string Symbol { get; private set; }
    public ShipRole Role { get; private set; }
    public ShipStatus Status { get; private set; }
    public FlightMode FlightMode { get; private set; }
    public WaypointSymbol CurrentWaypoint { get; private set; }
    public SystemSymbol CurrentSystem { get; private set; }
    public Fuel Fuel { get; private set; }
    public Cargo Cargo { get; private set; }
    public ShipAssignment? Assignment { get; private set; }
    public DateTimeOffset? ArrivesAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    private Ship(
        string symbol,
        ShipRole role,
        ShipStatus status,
        FlightMode flightMode,
        WaypointSymbol currentWaypoint,
        SystemSymbol currentSystem,
        Fuel fuel,
        Cargo cargo,
        ShipAssignment? assignment,
        DateTimeOffset? arrivesAt,
        DateTimeOffset lastSyncedAt)
    {
        Symbol = symbol;
        Role = role;
        Status = status;
        FlightMode = flightMode;
        CurrentWaypoint = currentWaypoint;
        CurrentSystem = currentSystem;
        Fuel = fuel;
        Cargo = cargo;
        Assignment = assignment;
        ArrivesAt = arrivesAt;
        LastSyncedAt = lastSyncedAt;
    }

    public static Ship Reconstitute(
        string symbol,
        ShipRole role,
        ShipStatus status,
        FlightMode flightMode,
        WaypointSymbol currentWaypoint,
        SystemSymbol currentSystem,
        Fuel fuel,
        Cargo cargo,
        ShipAssignment? assignment = null,
        DateTimeOffset? arrivesAt = null,
        DateTimeOffset? lastSyncedAt = null)
        => new(symbol, role, status, flightMode, currentWaypoint, currentSystem, fuel, cargo,
               assignment, arrivesAt, lastSyncedAt ?? DateTimeOffset.UtcNow);

    public void UpdateFuel(Fuel fuel)
    {
        Fuel = fuel;
        if (fuel.Percentage < FuelLowThreshold)
            RaiseDomainEvent(new ShipFuelLowEvent(Symbol, fuel.Current, fuel.Capacity));
    }

    public void UpdateNav(ShipStatus status, WaypointSymbol waypoint, SystemSymbol system, DateTimeOffset? arrivesAt)
    {
        var wasInTransit = Status == ShipStatus.InTransit;
        Status = status;
        CurrentWaypoint = waypoint;
        CurrentSystem = system;
        ArrivesAt = arrivesAt;

        if (wasInTransit && status != ShipStatus.InTransit)
            RaiseDomainEvent(new ShipArrivedAtWaypointEvent(Symbol, waypoint));
    }

    public void CompleteAssignment()
    {
        if (Assignment is null) return;
        RaiseDomainEvent(new ShipAssignmentCompletedEvent(Symbol, Assignment.Type));
        Assignment = null;
    }

    public void Assign(ShipAssignment assignment)
    {
        Assignment = assignment;
    }
}

using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4b: tests for the cross-cutting fuel-recovery ship planner.
/// </summary>
public sealed class FuelRecoveryShipPlannerTests
{
    private static readonly FuelRecoveryShipPlanner Planner = new();

    private static ShipAssignmentDto Assignment() =>
        new("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(
        string status = "IN_ORBIT",
        int fuelCurrent = 80,
        int fuelCapacity = 100,
        string flightMode = "CRUISE",
        string waypoint = "X1-AB-A") =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity);

    [Fact]
    public void CanPlan_ReturnsTrue_ForAnyAssignment()
    {
        Planner.CanPlan(Ship(), Assignment()).Should().BeTrue();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenDocked()
    {
        var decision = Planner.Plan(Ship(status: "DOCKED"), Assignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenDocked_AndCurrentWaypointDoesNotSellFuel()
    {
        var ship = Ship(status: "DOCKED", fuelCurrent: 0, fuelCapacity: 100);
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { CurrentWaypointSellsFuel = false });
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_RefuelsDockedShip_AtFuelMarket()
    {
        var ship = Ship(status: "DOCKED", fuelCurrent: 0, fuelCapacity: 100);
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { CurrentWaypointSellsFuel = true });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Refuel);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenDockedTankFull()
    {
        var ship = Ship(status: "DOCKED", fuelCurrent: 100, fuelCapacity: 100);
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { CurrentWaypointSellsFuel = true });
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenFuelLevelHealthy()
    {
        var decision = Planner.Plan(Ship(fuelCurrent: 80, fuelCapacity: 100), Assignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_PatchesDrift_ForZeroFuelCapacityShip()
    {
        var ship = Ship(fuelCurrent: 0, fuelCapacity: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }

    [Fact]
    public void Plan_ReturnsNone_WhenCriticalAndNoKnownFuelMarket()
    {
        var ship = Ship(fuelCurrent: 5, fuelCapacity: 100, flightMode: "DRIFT");
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { FuelMarketWaypoint = string.Empty });
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_PatchesDrift_BeforeNavigatingToFuelMarket()
    {
        var ship = Ship(fuelCurrent: 5, fuelCapacity: 100, flightMode: "CRUISE", waypoint: "X1-AB-A");
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { FuelMarketWaypoint = "X1-AB-FUEL" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }

    [Fact]
    public void Plan_DocksAtFuelMarket_WhenAlreadyThere()
    {
        var ship = Ship(fuelCurrent: 5, fuelCapacity: 100, flightMode: "DRIFT", waypoint: "X1-AB-FUEL");
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { FuelMarketWaypoint = "X1-AB-FUEL" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_NavigatesToFuelMarket_WhenAway()
    {
        var ship = Ship(fuelCurrent: 0, fuelCapacity: 100, flightMode: "DRIFT", waypoint: "X1-AB-A");
        var decision = Planner.Plan(ship, Assignment(), new ShipPlannerContext { FuelMarketWaypoint = "X1-AB-FUEL" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-FUEL");
    }
}

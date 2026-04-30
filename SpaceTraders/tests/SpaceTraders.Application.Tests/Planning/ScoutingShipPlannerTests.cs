using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4: tests for the scouting ship planner's pure decision logic.
/// </summary>
public sealed class ScoutingShipPlannerTests
{
    private static readonly ScoutingShipPlanner Planner = new();

    private static ShipAssignmentDto ScoutAssignment(string? origin = "X1-AB-TGT", string type = "Scout") =>
        new("SHIP-1", type, origin, null, null, null, 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(
        string waypoint,
        string status = "IN_ORBIT",
        int fuelCurrent = 80,
        int fuelCapacity = 100,
        string flightMode = "CRUISE") =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity);

    [Fact]
    public void CanPlan_ReturnsTrue_ForScoutAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-TGT"), ScoutAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsTrue_ForMarketProbeAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-TGT"), ScoutAssignment(type: "MarketProbe")).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsFalse_ForOtherAssignment()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Mine", "A", "B", null, null, 0, DateTimeOffset.UtcNow, null);
        Planner.CanPlan(Ship("A"), assignment).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenInTransit()
    {
        var ship = Ship("X1-AB-X", status: "IN_TRANSIT");
        Planner.Plan(ship, ScoutAssignment(), new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsDock_WhenAtScoutTarget()
    {
        var ship = Ship("X1-AB-TGT");
        var decision = Planner.Plan(ship, ScoutAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsNavigate_WhenAwayFromTarget()
    {
        var ship = Ship("X1-AB-OTHER");
        var decision = Planner.Plan(ship, ScoutAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-TGT");
    }

    [Fact]
    public void Plan_ReturnsAssignIdle_WhenNoTargetWaypoint()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Scout", null, null, null, null, 0, DateTimeOffset.UtcNow, null);
        var ship = Ship("X1-AB-X");
        Planner.Plan(ship, assignment, new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_PatchesDrift_WhenZeroFuelCapacity()
    {
        var ship = Ship("X1-AB-OTHER", fuelCurrent: 0, fuelCapacity: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, ScoutAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }
}

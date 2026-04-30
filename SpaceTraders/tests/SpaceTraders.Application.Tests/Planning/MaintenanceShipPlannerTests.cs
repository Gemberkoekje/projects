using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4b: tests for the cross-cutting maintenance ship planner.
/// </summary>
public sealed class MaintenanceShipPlannerTests
{
    private static readonly MaintenanceShipPlanner Planner = new();

    private static ShipAssignmentDto Assignment(string type = "Trade") =>
        new("SHIP-1", type, "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(string status = "DOCKED") =>
        new("SHIP-1", "X1-AB", "X1-AB-A", status, "CRUISE", 80, 100);

    [Fact]
    public void Plan_ReturnsNone_WhenInOrbit()
    {
        var decision = Planner.Plan(
            Ship(status: "IN_ORBIT"),
            Assignment(),
            new ShipPlannerContext { Maintenance = new FleetMaintenanceDecision(true, false, false, "needs repair") });
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenNoMaintenanceDecision()
    {
        var decision = Planner.Plan(Ship(), Assignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_Scrap_TakesPrecedenceOverRepair()
    {
        var decision = Planner.Plan(
            Ship(),
            Assignment(),
            new ShipPlannerContext { Maintenance = new FleetMaintenanceDecision(ShouldRepair: true, ShouldScrap: true, AvoidLongRoutes: false, "scrap it") });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Scrap);
    }

    [Fact]
    public void Plan_Repairs_WhenRepairFlagSet()
    {
        var decision = Planner.Plan(
            Ship(),
            Assignment(),
            new ShipPlannerContext { Maintenance = new FleetMaintenanceDecision(ShouldRepair: true, ShouldScrap: false, AvoidLongRoutes: false, "repair it") });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Repair);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenNoActionRequired()
    {
        var decision = Planner.Plan(
            Ship(),
            Assignment(),
            new ShipPlannerContext { Maintenance = new FleetMaintenanceDecision(false, false, false) });
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }
}

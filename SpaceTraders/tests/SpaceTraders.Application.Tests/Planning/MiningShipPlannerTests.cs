using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 3: tests for the mining ship planner's pure decision logic.
/// </summary>
public sealed class MiningShipPlannerTests
{
    private static readonly MiningShipPlanner Planner = new();

    private static ShipAssignmentDto MineAssignment(string origin = "X1-AB-AST", string? dest = "X1-AB-MKT") =>
        new("SHIP-1", "Mine", origin, dest, "IRON_ORE", null, 0, DateTimeOffset.UtcNow, null);

    private static ShipAssignmentDto SiphonAssignment() =>
        new("SHIP-1", "Siphon", "X1-AB-GAS", "X1-AB-MKT", "HYDROCARBON", null, 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(
        string waypoint,
        string status = "IN_ORBIT",
        int cargo = 0,
        int capacity = 40,
        IReadOnlyList<string>? mounts = null,
        int fuelCurrent = 80,
        int fuelCapacity = 100,
        string flightMode = "CRUISE") =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity,
            CargoCurrent: cargo, CargoCapacity: capacity, MountSymbols: mounts);

    [Fact]
    public void CanPlan_ReturnsTrue_ForMineAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-AST"), MineAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsTrue_ForSiphonAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-GAS"), SiphonAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsFalse_ForOtherAssignmentTypes()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null);
        Planner.CanPlan(Ship("X1-AB-A"), assignment).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenShipInTransit()
    {
        var ship = Ship("X1-AB-AST", status: "IN_TRANSIT");
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsNone_WhenDocked()
    {
        var ship = Ship("X1-AB-AST", status: "DOCKED");
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsSurvey_WhenAtOriginWithSurveyEquipmentAndNoActiveSurvey()
    {
        var ship = Ship("X1-AB-AST", mounts: ["MOUNT_SURVEYOR_I", "MOUNT_MINING_LASER_I"]);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext { ActiveSurveyCount = 0 });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Survey);
    }

    [Fact]
    public void Plan_ReturnsExtract_WhenAtOriginWithoutSurveyEquipment()
    {
        var ship = Ship("X1-AB-AST", mounts: ["MOUNT_MINING_LASER_I"]);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Extract);
    }

    [Fact]
    public void Plan_ReturnsExtract_WhenAtOriginWithActiveSurvey()
    {
        var ship = Ship("X1-AB-AST", mounts: ["MOUNT_SURVEYOR_I", "MOUNT_MINING_LASER_I"]);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext { ActiveSurveyCount = 1 });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Extract);
    }

    [Fact]
    public void Plan_ReturnsSiphon_WhenAtSiphonOriginWithCargoSpace()
    {
        var ship = Ship("X1-AB-GAS");
        var decision = Planner.Plan(ship, SiphonAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Siphon);
    }

    [Fact]
    public void Plan_ReturnsDock_WhenAtSellDestinationWithCargo()
    {
        var ship = Ship("X1-AB-MKT", cargo: 10);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsNavigate_WhenCargoButNotAtSellDestination()
    {
        // Cargo is full so the at-origin extract branch does not apply.
        var ship = Ship("X1-AB-AST", cargo: 40, capacity: 40);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-MKT");
    }

    [Fact]
    public void Plan_ReturnsAssignIdle_WhenCargoButNoSellDestination()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", null, null, null, 0, DateTimeOffset.UtcNow, null);
        var ship = Ship("X1-AB-AST", cargo: 10, capacity: 10);
        var decision = Planner.Plan(ship, assignment, new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_ReturnsNavigate_WhenEmptyCargoAndNotAtOrigin()
    {
        var ship = Ship("X1-AB-MKT");
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-AST");
    }

    [Fact]
    public void Plan_ReturnsDock_WhenEmptyCargoAtOrigin_WithFullCargoCapacityZero()
    {
        var ship = Ship("X1-AB-AST", cargo: 0, capacity: 0);
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsPatchFlightMode_WhenCargoAndDriftRequired_DueToZeroFuelCapacity()
    {
        var ship = Ship("X1-AB-AST", cargo: 40, capacity: 40, fuelCurrent: 0, fuelCapacity: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }

    [Fact]
    public void Plan_ReturnsPatchFlightMode_WhenRecommendedDiffersAndSameSystem()
    {
        var ship = Ship("X1-AB-AST", cargo: 40, capacity: 40, flightMode: "CRUISE");
        var decision = Planner.Plan(
            ship,
            MineAssignment(),
            new ShipPlannerContext { RecommendedFlightMode = "BURN" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("BURN");
    }

    [Fact]
    public void Plan_DoesNotPatchFlightMode_WhenAlreadyMatchesRecommendation()
    {
        var ship = Ship("X1-AB-AST", cargo: 40, capacity: 40, flightMode: "CRUISE");
        var decision = Planner.Plan(
            ship,
            MineAssignment(),
            new ShipPlannerContext { RecommendedFlightMode = "CRUISE" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
    }
}

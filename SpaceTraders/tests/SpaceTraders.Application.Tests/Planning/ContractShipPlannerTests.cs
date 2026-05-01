using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4: tests for the contract ship planner's pure decision logic.
/// </summary>
public sealed class ContractShipPlannerTests
{
    private static readonly ContractShipPlanner Planner = new();

    private static ShipAssignmentDto ContractAssignment(string? origin = "X1-AB-LOAD", string? dest = "X1-AB-DELIVER", string cargo = "ALUMINUM_ORE") =>
        new("SHIP-1", "Contract", origin, dest, cargo, "CT-1", 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(
        string waypoint,
        string status = "IN_ORBIT",
        IReadOnlyList<CargoItemModel>? inventory = null,
        int fuelCurrent = 80,
        int fuelCapacity = 100,
        string flightMode = "CRUISE") =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity,
            CargoCurrent: inventory?.Sum(i => i.Units) ?? 0, CargoCapacity: 40, CargoInventory: inventory);

    [Fact]
    public void CanPlan_ReturnsTrue_ForContractAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-LOAD"), ContractAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsFalse_ForOtherAssignment()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Trade", "A", "B", null, null, 0, DateTimeOffset.UtcNow, null);
        Planner.CanPlan(Ship("A"), assignment).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenInTransit()
    {
        var ship = Ship("X1-AB-LOAD", status: "IN_TRANSIT");
        Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsDock_WhenAtLoadWaypointWithoutContractCargo()
    {
        var ship = Ship("X1-AB-LOAD");
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_NavigatesToDelivery_WhenCarryingContractGood()
    {
        var ship = Ship("X1-AB-LOAD", inventory: [new CargoItemModel("ALUMINUM_ORE", 10)]);
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-DELIVER");
    }

    [Fact]
    public void Plan_DocksAtDelivery_WhenAtDeliveryWithCargo()
    {
        var ship = Ship("X1-AB-DELIVER", inventory: [new CargoItemModel("ALUMINUM_ORE", 10)]);
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsAssignIdle_WhenNoOriginAndNoDestination()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Contract", null, null, "ALUMINUM_ORE", "CT-1", 0, DateTimeOffset.UtcNow, null);
        var ship = Ship("X1-AB-X");
        Planner.Plan(ship, assignment, new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_PatchesDrift_WhenZeroFuelCapacity()
    {
        var ship = Ship("X1-AB-X", fuelCurrent: 0, fuelCapacity: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }

    // ---- Phase 6.5b: docked-state tests ----

    [Fact]
    public void Plan_ReturnsBuyCargo_WhenDockedAtOriginNeedingCargo()
    {
        var ship = Ship("X1-AB-LOAD", status: "DOCKED");
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.BuyCargo);
        decision.TradeSymbol.Should().Be("ALUMINUM_ORE");
    }

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedAtOriginWithFullRequiredCargo()
    {
        var ship = Ship("X1-AB-LOAD", status: "DOCKED",
            inventory: [new CargoItemModel("ALUMINUM_ORE", 20)]);
        var assignment = new ShipAssignmentDto("SHIP-1", "Contract", "X1-AB-LOAD", "X1-AB-DELIVER", "ALUMINUM_ORE", "CT-1", 0, DateTimeOffset.UtcNow, null, RequiredUnits: 20);
        var decision = Planner.Plan(ship, assignment, new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ReturnsDeliverContractCargo_WhenDockedAtDestinationWithCargo()
    {
        var ship = Ship("X1-AB-DELIVER", status: "DOCKED",
            inventory: [new CargoItemModel("ALUMINUM_ORE", 10)]);
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.DeliverContractCargo);
        decision.TradeSymbol.Should().Be("ALUMINUM_ORE");
        decision.ContractId.Should().Be("CT-1");
    }

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedAtDestinationWithNoCargo()
    {
        var ship = Ship("X1-AB-DELIVER", status: "DOCKED");
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedElsewhere()
    {
        var ship = Ship("X1-AB-OTHER", status: "DOCKED");
        var decision = Planner.Plan(ship, ContractAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }
}

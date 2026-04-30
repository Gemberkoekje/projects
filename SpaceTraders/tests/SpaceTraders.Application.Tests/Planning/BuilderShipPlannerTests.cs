using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4b: tests for the builder/construction ship planner's pure decision logic.
/// </summary>
public sealed class BuilderShipPlannerTests
{
    private static readonly BuilderShipPlanner Planner = new();

    private static ShipAssignmentDto BuilderAssignment(
        string origin = "X1-AB-MKT",
        string dest = "X1-AB-CC00",
        string cargo = "QUARTZ_SAND",
        int requiredUnits = 20,
        bool supplyCompleted = false) =>
        new("SHIP-1", "Builder", origin, dest, cargo, null, 0, DateTimeOffset.UtcNow, null,
            PurchaseUnitPrice: 0, RequiredUnits: requiredUnits, SupplyCompleted: supplyCompleted);

    private static ShipModel Ship(
        string waypoint,
        string status = "IN_ORBIT",
        int cargo = 0,
        int capacity = 40,
        IReadOnlyList<CargoItemModel>? inventory = null) =>
        new("SHIP-1", "X1-AB", waypoint, status, "CRUISE", 80, 100,
            CargoCurrent: cargo, CargoCapacity: capacity, CargoInventory: inventory);

    private static IReadOnlyList<CargoItemModel> Inventory(string symbol, int units) =>
        [new CargoItemModel(symbol, units)];

    [Fact]
    public void CanPlan_ReturnsTrue_ForBuilderAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-MKT"), BuilderAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsFalse_ForOtherAssignmentTypes()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null);
        Planner.CanPlan(Ship("X1-AB-A"), assignment).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenInTransit()
    {
        var ship = Ship("X1-AB-MKT", status: "IN_TRANSIT");
        var decision = Planner.Plan(ship, BuilderAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_AssignsIdle_WhenAssignmentMissingDestination()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Builder", "X1-AB-MKT", null, "QUARTZ_SAND", null, 0, DateTimeOffset.UtcNow, null);
        var decision = Planner.Plan(Ship("X1-AB-MKT", status: "DOCKED"), assignment, new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_AssignsIdle_WhenConstructionComplete()
    {
        var decision = Planner.Plan(Ship("X1-AB-MKT", status: "DOCKED"), BuilderAssignment(), new ShipPlannerContext { ConstructionComplete = true });
        decision.Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_Docked_AssignsIdle_WhenSupplyCompleted()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED");
        var decision = Planner.Plan(ship, BuilderAssignment(supplyCompleted: true), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_Docked_BuysCargo_WhenShortOnMaterials()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 0, capacity: 40);
        var decision = Planner.Plan(ship, BuilderAssignment(requiredUnits: 20), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.BuyCargo);
        decision.TradeSymbol.Should().Be("QUARTZ_SAND");
        decision.Units.Should().Be(20);
    }

    [Fact]
    public void Plan_Docked_Orbits_WhenLoaded()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 20, capacity: 40,
            inventory: Inventory("QUARTZ_SAND", 20));
        var decision = Planner.Plan(ship, BuilderAssignment(requiredUnits: 20), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_InOrbit_SuppliesAtConstructionSite_WhenCarryingCargo()
    {
        var ship = Ship("X1-AB-CC00", status: "IN_ORBIT", cargo: 20, capacity: 40,
            inventory: Inventory("QUARTZ_SAND", 20));
        var decision = Planner.Plan(ship, BuilderAssignment(requiredUnits: 20), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.SupplyConstruction);
        decision.WaypointSymbol.Should().Be("X1-AB-CC00");
        decision.SystemSymbol.Should().Be("X1-AB");
        decision.TradeSymbol.Should().Be("QUARTZ_SAND");
        decision.Units.Should().Be(20);
    }

    [Fact]
    public void Plan_InOrbit_NavigatesToOrigin_WhenAtSiteWithoutCargo()
    {
        var ship = Ship("X1-AB-CC00", status: "IN_ORBIT", cargo: 0);
        var decision = Planner.Plan(ship, BuilderAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-MKT");
    }

    [Fact]
    public void Plan_InOrbit_NavigatesToConstructionSite_WhenCarryingCargoElsewhere()
    {
        var ship = Ship("X1-AB-OTHER", status: "IN_ORBIT", cargo: 20, capacity: 40,
            inventory: Inventory("QUARTZ_SAND", 20));
        var decision = Planner.Plan(ship, BuilderAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-CC00");
    }

    [Fact]
    public void Plan_InOrbit_Docks_WhenAtOriginWithoutCargo()
    {
        var ship = Ship("X1-AB-MKT", status: "IN_ORBIT", cargo: 0);
        var decision = Planner.Plan(ship, BuilderAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_InOrbit_NavigatesToOrigin_WhenElsewhereWithoutCargo()
    {
        var ship = Ship("X1-AB-OTHER", status: "IN_ORBIT", cargo: 0);
        var decision = Planner.Plan(ship, BuilderAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-MKT");
    }
}

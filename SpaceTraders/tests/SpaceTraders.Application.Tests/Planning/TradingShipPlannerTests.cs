using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 4: tests for the trading ship planner's pure decision logic.
/// </summary>
public sealed class TradingShipPlannerTests
{
    private static readonly TradingShipPlanner Planner = new();

    private static ShipAssignmentDto TradeAssignment(string? origin = "X1-AB-BUY", string? dest = "X1-AB-SELL") =>
        new("SHIP-1", "Trade", origin, dest, "FUEL", null, 0, DateTimeOffset.UtcNow, null);

    private static ShipModel Ship(
        string waypoint,
        string status = "IN_ORBIT",
        int cargo = 0,
        int fuelCurrent = 80,
        int fuelCapacity = 100,
        string flightMode = "CRUISE",
        IReadOnlyList<CargoItemModel>? cargoInventory = null) =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity,
            CargoCurrent: cargo, CargoCapacity: 40, CargoInventory: cargoInventory);

    [Fact]
    public void CanPlan_ReturnsTrue_ForTradeAssignment()
    {
        Planner.CanPlan(Ship("X1-AB-BUY"), TradeAssignment()).Should().BeTrue();
    }

    [Fact]
    public void CanPlan_ReturnsFalse_ForOtherAssignment()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null);
        Planner.CanPlan(Ship("X1-AB-A"), assignment).Should().BeFalse();
    }

    [Fact]
    public void Plan_ReturnsNone_WhenInTransit()
    {
        var ship = Ship("X1-AB-BUY", status: "IN_TRANSIT");
        Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public void Plan_ReturnsBuyCargo_WhenDockedAtBuyWaypoint()
    {
        var ship = Ship("X1-AB-BUY", status: "DOCKED");
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.BuyCargo);
        decision.TradeSymbol.Should().Be("FUEL");
    }

    [Fact]
    public void Plan_ReturnsDock_WhenAtBuyWaypointWithEmptyCargo()
    {
        var ship = Ship("X1-AB-BUY", cargo: 0);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsDock_WhenAtSellWaypointWithCargo()
    {
        var ship = Ship("X1-AB-SELL", cargo: 20);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Dock);
    }

    [Fact]
    public void Plan_ReturnsNavigateToSell_WhenCargoAndNotAtSell()
    {
        var ship = Ship("X1-AB-BUY", cargo: 20);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-SELL");
    }

    [Fact]
    public void Plan_ReturnsNavigateToBuy_WhenEmptyCargoAndNotAtBuy()
    {
        var ship = Ship("X1-AB-SELL", cargo: 0);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        decision.DestinationWaypoint.Should().Be("X1-AB-BUY");
    }

    [Fact]
    public void Plan_ReturnsAssignIdle_WhenAssignmentHasNoWaypoints()
    {
        var assignment = new ShipAssignmentDto("SHIP-1", "Trade", null, null, null, null, 0, DateTimeOffset.UtcNow, null);
        var ship = Ship("X1-AB-X", cargo: 0);
        Planner.Plan(ship, assignment, new ShipPlannerContext()).Kind.Should().Be(ShipPlannerCommandKind.AssignIdle);
    }

    [Fact]
    public void Plan_ReturnsPatchFlightMode_WhenZeroFuelCapacityAndNotDrift()
    {
        var ship = Ship("X1-AB-SELL", cargo: 0, fuelCurrent: 0, fuelCapacity: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("DRIFT");
    }

    [Fact]
    public void Plan_PatchesFlightMode_WhenRecommendedDiffers()
    {
        var ship = Ship("X1-AB-SELL", cargo: 0, flightMode: "CRUISE");
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext { RecommendedFlightMode = "BURN" });
        decision.Kind.Should().Be(ShipPlannerCommandKind.PatchFlightMode);
        decision.FlightMode.Should().Be("BURN");
    }

    // ---- Phase 6.5b: docked-state tests ----

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedAtBuyWaypointWithFullCargo()
    {
        var ship = Ship("X1-AB-BUY", status: "DOCKED", cargo: 40);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ReturnsDeliverContractCargo_WhenDockedAtSellWaypointWithContractDeliverable()
    {
        var ship = Ship("X1-AB-SELL", status: "DOCKED", cargo: 10,
            cargoInventory: [new CargoItemModel("FUEL", 10)]);
        var contract = new ContractDto("CT-1", "FAC", "PROCUREMENT", true, false, null, null, null,
            """[{"TradeSymbol":"FUEL","DestinationSymbol":"X1-AB-SELL","UnitsRequired":20,"UnitsFulfilled":0}]""");
        var context = new ShipPlannerContext { ActiveContracts = [contract] };
        var decision = Planner.Plan(ship, TradeAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.DeliverContractCargo);
        decision.ContractId.Should().Be("CT-1");
    }

    [Fact]
    public void Plan_ReturnsSellCargo_WhenDockedAtSellWaypointWithCargoAndNoContracts()
    {
        var ship = Ship("X1-AB-SELL", status: "DOCKED", cargo: 15,
            cargoInventory: [new CargoItemModel("FUEL", 15)]);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.SellCargo);
        decision.TradeSymbol.Should().Be("FUEL");
        decision.Units.Should().Be(15);
    }

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedAtSellWaypointWithNoCargoAndNoContracts()
    {
        var ship = Ship("X1-AB-SELL", status: "DOCKED", cargo: 0);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ReturnsOrbit_WhenDockedElsewhere()
    {
        var ship = Ship("X1-AB-OTHER", status: "DOCKED", cargo: 0);
        var decision = Planner.Plan(ship, TradeAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }
}

using FluentAssertions;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
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
        string flightMode = "CRUISE",
        IReadOnlyList<CargoItemModel>? cargoInventory = null) =>
        new("SHIP-1", "X1-AB", waypoint, status, flightMode, fuelCurrent, fuelCapacity,
            CargoCurrent: cargo, CargoCapacity: capacity, MountSymbols: mounts, CargoInventory: cargoInventory);

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
    public void Plan_ReturnsOrbit_WhenDockedAtOriginWithNoCargo()
    {
        var ship = Ship("X1-AB-AST", status: "DOCKED");
        var decision = Planner.Plan(ship, MineAssignment(), new ShipPlannerContext());
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
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

    // ---- Phase 6.5b: docked-state tests ----

    [Fact]
    public void Plan_ReturnsDeliverContractCargo_WhenDockedWithDeliverableContractAtWaypoint()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10,
            cargoInventory: [new CargoItemModel("IRON_ORE", 10)]);
        var contract = new ContractDto("CT-1", "FAC", "PROCUREMENT", true, false, null, null, null,
            """[{"TradeSymbol":"IRON_ORE","DestinationSymbol":"X1-AB-MKT","UnitsRequired":20,"UnitsFulfilled":0}]""");
        var context = new ShipPlannerContext { ActiveContracts = [contract] };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.DeliverContractCargo);
        decision.TradeSymbol.Should().Be("IRON_ORE");
        decision.Units.Should().Be(10);
        decision.ContractId.Should().Be("CT-1");
    }

    [Fact]
    public void Plan_ReturnsSellCargo_WhenDockedWithEligibleNonProtectedCargo()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10,
            cargoInventory: [new CargoItemModel("IRON_ORE", 10)]);
        var snapshot = new SpaceTraders.Application.Interfaces.MarketSnapshot(
            "X1-AB-MKT", "X1-AB",
            [new SpaceTraders.Application.Interfaces.TradeGoodSnapshot("IRON_ORE", "EXCHANGE", 50, 300, 10, "MODERATE", "GROWING")],
            [], [], []);
        var context = new ShipPlannerContext { CurrentMarketSnapshot = snapshot, MiningMinimumSellPrice = 100 };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.SellCargo);
        decision.TradeSymbol.Should().Be("IRON_ORE");
        decision.Units.Should().Be(10);
    }

    [Fact]
    public void Plan_SkipsSellCargo_WhenCargoIsBelowMinimumSellPrice()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10,
            cargoInventory: [new CargoItemModel("IRON_ORE", 10)]);
        var snapshot = new SpaceTraders.Application.Interfaces.MarketSnapshot(
            "X1-AB-MKT", "X1-AB",
            [new SpaceTraders.Application.Interfaces.TradeGoodSnapshot("IRON_ORE", "EXCHANGE", 5, 50, 10, "WEAK", "DECLINING")],
            [], [], []);
        var context = new ShipPlannerContext { CurrentMarketSnapshot = snapshot, MiningMinimumSellPrice = 100 };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        // Hold is not full so jettison does not apply either; just orbits.
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ReturnsJettisonCargo_WhenFullAndLowValueAndJettisonEnabled()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10, capacity: 10,
            cargoInventory: [new CargoItemModel("ROCK", 10)]);
        var snapshot = new SpaceTraders.Application.Interfaces.MarketSnapshot(
            "X1-AB-MKT", "X1-AB",
            [new SpaceTraders.Application.Interfaces.TradeGoodSnapshot("ROCK", "EXCHANGE", 1, 5, 10, "ABUNDANT", "DECLINING")],
            [], [], []);
        var context = new ShipPlannerContext
        {
            CurrentMarketSnapshot = snapshot,
            MiningMinimumSellPrice = 100,
            MiningJettisonLowValueWhenFull = true,
        };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.JettisonCargo);
        decision.TradeSymbol.Should().Be("ROCK");
    }

    [Fact]
    public void Plan_ReturnsRefuelFromCargo_WhenHydrocarbonAboveReserveAndFuelNotFull()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10, capacity: 40,
            fuelCurrent: 50, fuelCapacity: 100,
            cargoInventory: [new CargoItemModel("HYDROCARBON", 10)]);
        var context = new ShipPlannerContext { MiningReserveHydrocarbonUnits = 5 };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.RefuelFromCargo);
    }

    [Fact]
    public void Plan_SkipsRefuelFromCargo_WhenHydrocarbonAtOrBelowReserve()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 5, capacity: 40,
            fuelCurrent: 50, fuelCapacity: 100,
            cargoInventory: [new CargoItemModel("HYDROCARBON", 5)]);
        var context = new ShipPlannerContext { MiningReserveHydrocarbonUnits = 5 };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }

    [Fact]
    public void Plan_ProtectsContractCargo_FromSell()
    {
        var ship = Ship("X1-AB-MKT", status: "DOCKED", cargo: 10,
            cargoInventory: [new CargoItemModel("IRON_ORE", 10)]);
        // Contract protects IRON_ORE (undeliverable at this waypoint, but still in the protected set)
        var contract = new ContractDto("CT-1", "FAC", "PROCUREMENT", true, false, null, null, null,
            """[{"TradeSymbol":"IRON_ORE","DestinationSymbol":"X1-AB-OTHER","UnitsRequired":20,"UnitsFulfilled":0}]""");
        var snapshot = new SpaceTraders.Application.Interfaces.MarketSnapshot(
            "X1-AB-MKT", "X1-AB",
            [new SpaceTraders.Application.Interfaces.TradeGoodSnapshot("IRON_ORE", "EXCHANGE", 50, 300, 10, "MODERATE", "GROWING")],
            [], [], []);
        var context = new ShipPlannerContext
        {
            ActiveContracts = [contract],
            CurrentMarketSnapshot = snapshot,
            MiningMinimumSellPrice = 100,
        };
        var decision = Planner.Plan(ship, MineAssignment(), context);
        // Iron ore is protected; nothing to sell/jettison → orbit.
        decision.Kind.Should().Be(ShipPlannerCommandKind.Orbit);
    }
}

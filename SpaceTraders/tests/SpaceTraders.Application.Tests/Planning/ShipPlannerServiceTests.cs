using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using Wolverine;

namespace SpaceTraders.Application.Tests.Planning;

/// <summary>
/// Phase 3: tests for the planner service that translates planner decisions into commands
/// via the existing command acceptors. Verifies the "one decision -> one command" rule.
/// </summary>
public sealed class ShipPlannerServiceTests
{
    private static ShipPlannerService Build(
        out IShipRepository shipsRepo,
        out IShipAssignmentRepository assignmentsRepo,
        out ISurveyRepository surveysRepo,
        out INavigationPlanningService navigation,
        out IInOrbitCommandAcceptor inOrbit,
        out IDockedCommandAcceptor docked,
        out IMessageBus bus,
        params IShipPlanner[] planners)
    {
        shipsRepo = Substitute.For<IShipRepository>();
        assignmentsRepo = Substitute.For<IShipAssignmentRepository>();
        surveysRepo = Substitute.For<ISurveyRepository>();
        navigation = Substitute.For<INavigationPlanningService>();
        inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        docked = Substitute.For<IDockedCommandAcceptor>();
        bus = Substitute.For<IMessageBus>();

        var constructions = Substitute.For<IConstructionRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var contractsRepo = Substitute.For<IContractRepository>();
        var settingsRepo = Substitute.For<ISettingsRepository>();
        var waypointVisit = Substitute.For<IWaypointVisitService>();
        var marketRefresh = Substitute.For<IMarketRefreshService>();
        var shipyardRefresh = Substitute.For<IShipyardRefreshService>();

        waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        contractsRepo.GetActiveAsync(Arg.Any<CancellationToken>())
            .Returns([]);
        var maintenance = Substitute.For<IFleetMaintenancePlanner>();
        maintenance.DecideAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FleetMaintenanceDecision(ShouldRepair: false, ShouldScrap: false, AvoidLongRoutes: false));

        return new ShipPlannerService(
            planners,
            shipsRepo,
            assignmentsRepo,
            surveysRepo,
            navigation,
            constructions,
            waypoints,
            markets,
            contractsRepo,
            settingsRepo,
            maintenance,
            waypointVisit,
            marketRefresh,
            shipyardRefresh,
            inOrbit,
            docked,
            bus,
            NullLogger<ShipPlannerService>.Instance);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenShipNotFound()
    {
        var service = Build(out var ships, out _, out _, out _, out var inOrbit, out var docked, out _, new MiningShipPlanner());
        ships.FindAsync("SHIP-X", Arg.Any<CancellationToken>()).Returns((ShipModel?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-X", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
        await docked.DidNotReceiveWithAnyArgs().OrbitAsync(default!, default);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenShipHasNoAssignment()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out _, out _, out _, new MiningShipPlanner());
        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-A", "IN_ORBIT", "CRUISE", 80, 100));
        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipAssignmentDto?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesExtract_ViaInOrbitAcceptor()
    {
        var service = Build(out var ships, out var assignments, out var surveys, out _, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100,
                CargoCurrent: 0, CargoCapacity: 40, MountSymbols: ["MOUNT_MINING_LASER_I"]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        surveys.GetActiveByWaypointAsync("X1-AB-AST", Arg.Any<CancellationToken>()).Returns([]);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.Extract);
        await inOrbit.Received(1).ExtractAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
        await inOrbit.DidNotReceiveWithAnyArgs().DockAsync(default!, default);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesNavigate_ViaInOrbitAcceptor()
    {
        var service = Build(out var ships, out var assignments, out _, out var navigation, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE", 80, 100,
                CargoCurrent: 40, CargoCapacity: 40));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        navigation.BuildPlanAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((NavigationPlan?)null);

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.Navigate);
        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-MKT", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAndExecuteAsync_ReturnsNone_WhenNoPlannerMatchesAssignment()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out var inOrbit, out _, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-A", "IN_ORBIT", "CRUISE", 80, 100));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-A", "X1-AB-B", null, null, 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.None);
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
    }

    // ---- Phase 6.5b: docked-command routing tests ----

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesSellCargo_ViaDockedAcceptor()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out _, out var docked, out _, new TradingShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-SELL", "DOCKED", "CRUISE", 80, 100,
                CargoCurrent: 10, CargoCapacity: 40, CargoInventory: [new CargoItemModel("FUEL", 10)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-BUY", "X1-AB-SELL", "FUEL", null, 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.SellCargo);
        await docked.Received(1).SellCargoAsync("SHIP-1", "FUEL", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesDeliverContractCargo_ViaDockedAcceptor()
    {
        var service = Build(out var ships, out var assignments, out _, out _, out _, out var docked, out _, new ContractShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-DELIVER", "DOCKED", "CRUISE", 80, 100,
                CargoCurrent: 10, CargoCapacity: 40, CargoInventory: [new CargoItemModel("IRON_ORE", 10)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Contract", "X1-AB-LOAD", "X1-AB-DELIVER", "IRON_ORE", "CT-1", 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.DeliverContractCargo);
        await docked.Received(1).DeliverContractAsync("CT-1", "SHIP-1", "IRON_ORE", 10, "X1-AB-DELIVER", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesRefuelFromCargo_ViaDockedAcceptor()
    {
        // Mining ship docked with hydrocarbon above reserve and fuel not full.
        var service = Build(out var ships, out var assignments, out _, out _, out _, out var docked, out _, new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 50, 100,
                CargoCurrent: 10, CargoCapacity: 40, CargoInventory: [new CargoItemModel("HYDROCARBON", 10)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.RefuelFromCargo);
        await docked.Received(1).RefuelAsync("SHIP-1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlanAndExecuteAsync_RoutesJettisonCargo_ViaBus()
    {
        // Use a stub planner that always returns JettisonCargo to verify executor routing.
        var stubPlanner = new StubJettisonCargoPlannerFor("SHIP-1", "ROCK", 5);
        var service = Build(out var ships, out var assignments, out _, out _, out _, out _, out var bus, stubPlanner);

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE", 80, 100,
                CargoCurrent: 5, CargoCapacity: 5));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        var decision = await service.PlanAndExecuteAsync("SHIP-1", CancellationToken.None);

        decision.Kind.Should().Be(ShipPlannerCommandKind.JettisonCargo);
        await bus.Received(1).InvokeAsync(
            Arg.Is<JettisonCargoCommand>(c => c.ShipSymbol == "SHIP-1" && c.TradeSymbol == "ROCK" && c.Units == 5),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>Stub planner that always emits JettisonCargo for a specific ship.</summary>
file sealed class StubJettisonCargoPlannerFor(string shipSymbol, string tradeSymbol, int units) : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment) => ship.Symbol == shipSymbol;

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
        => ShipPlannerDecision.JettisonCargo(ship.Symbol, tradeSymbol, units, "Test stub: jettison.");
}

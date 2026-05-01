using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Planning;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Tests.EventHandlers;

/// <summary>
/// Phase 6.5d regression tests: prove that <see cref="ShipAutomationTickEventHandler"/> +
/// <see cref="ShipPlannerService"/> fully covers docked, in-orbit, and in-transit decision
/// points for all ship roles without relying on chain-of-command handlers.
///
/// Each test wires the handler and planner together with mocked repositories/acceptors and
/// verifies exactly one command is issued (or none for in-transit) — no chain handler
/// instances are constructed or invoked.
/// </summary>
public sealed class Phase65dPlannerRegressionTests
{
    private static ShipAutomationTickEventHandler BuildHandler(
        out IInOrbitCommandAcceptor inOrbit,
        out IDockedCommandAcceptor docked,
        out IMessageBus bus,
        out IShipRepository ships,
        out IShipAssignmentRepository assignments,
        params IShipPlanner[] planners)
    {
        ships = Substitute.For<IShipRepository>();
        assignments = Substitute.For<IShipAssignmentRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var navigation = Substitute.For<INavigationPlanningService>();
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
        var maintenance = Substitute.For<IFleetMaintenancePlanner>();

        waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        contractsRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        maintenance.DecideAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FleetMaintenanceDecision(ShouldRepair: false, ShouldScrap: false, AvoidLongRoutes: false));

        var plannerService = new ShipPlannerService(
            planners,
            ships,
            assignments,
            surveys,
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

        return new ShipAutomationTickEventHandler(plannerService, NullLogger<ShipAutomationTickEventHandler>.Instance);
    }

    private static ShipAutomationTickEvent Tick(string shipSymbol = "SHIP-1") =>
        new(shipSymbol, "Test", DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.Empty);

    // ── Docked decision points ──────────────────────────────────────────────────

    [Fact]
    public async Task Docked_MiningShip_NoCargoAction_Orbits()
    {
        var handler = BuildHandler(out _, out var docked, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-MKT", "DOCKED", "CRUISE",
                100, 100, CargoCurrent: 0, CargoCapacity: 40));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
        await docked.DidNotReceiveWithAnyArgs().SellCargoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Docked_TradingShip_AtSellWaypointWithCargo_SellsCargo()
    {
        var handler = BuildHandler(out _, out var docked, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new TradingShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-SELL", "DOCKED", "CRUISE",
                80, 100,
                CargoCurrent: 5, CargoCapacity: 20,
                CargoInventory: [new CargoItemModel("FUEL", 5)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-BUY", "X1-AB-SELL", "FUEL", null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await docked.Received(1).SellCargoAsync("SHIP-1", "FUEL", 5, Arg.Any<CancellationToken>());
        await docked.DidNotReceiveWithAnyArgs().OrbitAsync(default!, default);
    }

    [Fact]
    public async Task Docked_ContractShip_AtOriginWithCargoSpace_BuysCargo()
    {
        var handler = BuildHandler(out _, out var docked, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new ContractShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-ORIGIN", "DOCKED", "CRUISE",
                80, 100, CargoCurrent: 0, CargoCapacity: 20));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Contract", "X1-AB-ORIGIN", "X1-AB-DEST", "IRON_ORE", "CT-1", 10, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await docked.Received(1).BuyCargoAsync("SHIP-1", "IRON_ORE", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Docked_ScoutShip_RefreshesDataAndOrbits()
    {
        var waypointVisit = Substitute.For<IWaypointVisitService>();
        var marketRefresh = Substitute.For<IMarketRefreshService>();
        var shipyardRefresh = Substitute.For<IShipyardRefreshService>();

        var ships = Substitute.For<IShipRepository>();
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var surveys = Substitute.For<ISurveyRepository>();
        var navigation = Substitute.For<INavigationPlanningService>();
        var docked = Substitute.For<IDockedCommandAcceptor>();
        var inOrbit = Substitute.For<IInOrbitCommandAcceptor>();
        var bus = Substitute.For<IMessageBus>();
        var constructions = Substitute.For<IConstructionRepository>();
        var waypoints = Substitute.For<IWaypointRepository>();
        var markets = Substitute.For<IMarketRepository>();
        var contractsRepo = Substitute.For<IContractRepository>();
        var settingsRepo = Substitute.For<ISettingsRepository>();
        var maintenance = Substitute.For<IFleetMaintenancePlanner>();

        waypoints.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns([]);
        contractsRepo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        maintenance.DecideAsync(Arg.Any<ShipModel>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FleetMaintenanceDecision(false, false, false));

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-SCOUT", "DOCKED", "CRUISE", 80, 100));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Scout", "X1-AB-SCOUT", null, null, null, 0, DateTimeOffset.UtcNow, null));

        var plannerService = new ShipPlannerService(
            [new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new ScoutingShipPlanner()],
            ships, assignments, surveys, navigation, constructions, waypoints, markets, contractsRepo,
            settingsRepo, maintenance, waypointVisit, marketRefresh, shipyardRefresh,
            inOrbit, docked, bus, NullLogger<ShipPlannerService>.Instance);

        var handler = new ShipAutomationTickEventHandler(plannerService, NullLogger<ShipAutomationTickEventHandler>.Instance);

        await handler.Handle(Tick(), CancellationToken.None);

        await waypointVisit.Received(1).MarkVisitedAsync("X1-AB-SCOUT", Arg.Any<CancellationToken>());
        await docked.Received(1).OrbitAsync("SHIP-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Docked_BuilderShip_NeedsMaterials_BuysCargo()
    {
        var handler = BuildHandler(out _, out var docked, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new BuilderShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-ORIGIN", "DOCKED", "CRUISE",
                80, 100, CargoCurrent: 0, CargoCapacity: 20));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Builder", "X1-AB-ORIGIN", "X1-AB-SITE", "STEEL", null, 10, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await docked.Received(1).BuyCargoAsync("SHIP-1", "STEEL", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── In-orbit decision points ────────────────────────────────────────────────

    [Fact]
    public async Task InOrbit_MiningShip_AtOriginWithCargoSpace_Extracts()
    {
        var handler = BuildHandler(out var inOrbit, out _, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_ORBIT", "CRUISE",
                80, 100, CargoCurrent: 0, CargoCapacity: 40));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.Received(1).ExtractAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
    }

    [Fact]
    public async Task InOrbit_TradingShip_NotAtDestination_Navigates()
    {
        var handler = BuildHandler(out var inOrbit, out _, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new TradingShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-OTHER", "IN_ORBIT", "CRUISE",
                80, 100, CargoCurrent: 0, CargoCapacity: 20));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Trade", "X1-AB-BUY", "X1-AB-SELL", "FUEL", null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-BUY", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceiveWithAnyArgs().DockAsync(default!, default);
    }

    [Fact]
    public async Task InOrbit_ContractShip_AtDestination_Docks()
    {
        var handler = BuildHandler(out var inOrbit, out _, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new ContractShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-DEST", "IN_ORBIT", "CRUISE",
                80, 100,
                CargoCurrent: 10, CargoCapacity: 20,
                CargoInventory: [new CargoItemModel("IRON_ORE", 10)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Contract", "X1-AB-LOAD", "X1-AB-DEST", "IRON_ORE", "CT-1", 10, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.Received(1).DockAsync("SHIP-1", Arg.Any<CancellationToken>());
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
    }

    [Fact]
    public async Task InOrbit_ScoutShip_NotAtTarget_Navigates()
    {
        var handler = BuildHandler(out var inOrbit, out _, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new ScoutingShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-OTHER", "IN_ORBIT", "CRUISE", 80, 100));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Scout", "X1-AB-TARGET", null, null, null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.Received(1).NavigateAsync("SHIP-1", "X1-AB-TARGET", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InOrbit_BuilderShip_AtSiteWithCargo_SuppliesConstruction()
    {
        var handler = BuildHandler(out var inOrbit, out _, out _, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new BuilderShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-SITE", "IN_ORBIT", "CRUISE",
                80, 100,
                CargoCurrent: 10, CargoCapacity: 20,
                CargoInventory: [new CargoItemModel("STEEL", 10)]));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Builder", "X1-AB-ORIGIN", "X1-AB-SITE", "STEEL", null, 10, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.Received(1).SupplyConstructionAsync(
            "SHIP-1", "X1-AB", "X1-AB-SITE", "STEEL", Arg.Any<int>(),
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── In-transit decision point ───────────────────────────────────────────────

    [Fact]
    public async Task InTransit_AnyShip_IssuesNoCommand()
    {
        var handler = BuildHandler(out var inOrbit, out var docked, out var bus, out var ships, out var assignments,
            new FuelRecoveryShipPlanner(), new MaintenanceShipPlanner(), new MiningShipPlanner());

        ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipModel("SHIP-1", "X1-AB", "X1-AB-AST", "IN_TRANSIT", "CRUISE", 80, 100));

        assignments.FindAsync("SHIP-1", Arg.Any<CancellationToken>())
            .Returns(new ShipAssignmentDto("SHIP-1", "Mine", "X1-AB-AST", "X1-AB-MKT", null, null, 0, DateTimeOffset.UtcNow, null));

        await handler.Handle(Tick(), CancellationToken.None);

        await inOrbit.DidNotReceiveWithAnyArgs().ExtractAsync(default!, default);
        await inOrbit.DidNotReceiveWithAnyArgs().NavigateAsync(default!, default!, default);
        await inOrbit.DidNotReceiveWithAnyArgs().DockAsync(default!, default);
        await docked.DidNotReceiveWithAnyArgs().OrbitAsync(default!, default);
        await bus.DidNotReceiveWithAnyArgs().InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    // ── Planner-only proof: no chain handler types are instantiated ─────────────

    [Fact]
    public void Phase65d_ChainHandlerTypes_AreNotRegisteredForDockedAndOrbitEvents()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var businessEffectEventTypes = new[]
        {
            typeof(ShipDockedEvent),
            typeof(ShipInOrbitEvent),
            typeof(ShipStateMismatchEvent),
        };

        foreach (var eventType in businessEffectEventTypes)
        {
            var handlerInterfaceType = typeof(IChainOfCommandEventHandler<>).MakeGenericType(eventType);
            services.Should().NotContain(
                d => d.ServiceType == handlerInterfaceType,
                $"Phase 6.5d: chain handlers for {eventType.Name} should not be registered; planner handles these.");
        }
    }
}

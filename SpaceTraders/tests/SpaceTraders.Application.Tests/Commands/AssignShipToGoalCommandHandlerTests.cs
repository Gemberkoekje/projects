using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events.Ships;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Tests.Commands;

/// <summary>
/// Phase 11a: unit tests for <see cref="AssignShipToGoalCommandHandler"/> covering each
/// <see cref="FleetGoalKind"/> branch.
/// </summary>
public sealed class AssignShipToGoalCommandHandlerTests
{
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IAssignmentResolver _resolver = Substitute.For<IAssignmentResolver>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();

    private AssignShipToGoalCommandHandler CreateHandler() =>
        new(_ships, _goals, _resolver, _settings, _shipyards, _bus,
            NullLogger<AssignShipToGoalCommandHandler>.Instance);

    private static ShipModel MakeShip(string symbol = "SHIP-1") =>
        new(symbol, "X1-AB", "X1-AB-001", "DOCKED", "CRUISE", 100, 100, CargoCapacity: 40);

    // ────────────────────────────────────────────────────────────────────────────
    // Contract goal
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Contract_ResolvesAndActivatesGoal()
    {
        var ship = MakeShip();
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);

        var resolvedGoal = new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-AST" };
        _resolver
            .ResolveAsync(ship, Arg.Any<ResourceProductionGoal>(), Arg.Any<CancellationToken>())
            .Returns(resolvedGoal);

        var fleetGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver iron",
            Priority: 100,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", resolvedGoal, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "GoalAssigned"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_Contract_PassesCorrectResourceProductionGoal()
    {
        var ship = MakeShip();
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        _resolver
            .ResolveAsync(Arg.Any<ShipModel>(), Arg.Any<ResourceProductionGoal>(), Arg.Any<CancellationToken>())
            .Returns(new MineResourceGoal { TradeSymbol = "IRON_ORE", SourceWaypointSymbol = "X1-AB-AST" });

        var fleetGoal = new FleetGoal(
            FleetGoalKind.Contract,
            "Deliver iron",
            Priority: 100,
            ContractId: "c1",
            TradeSymbol: "IRON_ORE",
            RemainingUnits: 50);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _resolver.Received(1).ResolveAsync(
            ship,
            Arg.Is<ResourceProductionGoal>(g =>
                g.TradeSymbol == "IRON_ORE" &&
                g.PurposeKind == ResourceProductionPurpose.Contract &&
                g.PurposeId == "c1" &&
                g.UnitsNeeded == 50),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Contract_WhenResolverReturnsNull_DoesNotSetGoal()
    {
        var ship = MakeShip();
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);
        _resolver
            .ResolveAsync(Arg.Any<ShipModel>(), Arg.Any<ResourceProductionGoal>(), Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        var fleetGoal = new FleetGoal(FleetGoalKind.Contract, "Deliver iron", 100, ContractId: "c1", TradeSymbol: "IRON_ORE");

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipAutomationTickEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_Contract_WhenShipNotFound_DoesNotSetGoal()
    {
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns((ShipModel?)null);

        var fleetGoal = new FleetGoal(FleetGoalKind.Contract, "Deliver iron", 100, ContractId: "c1", TradeSymbol: "IRON_ORE");

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Contract_WhenNoTradeSymbol_DoesNotSetGoal()
    {
        var fleetGoal = new FleetGoal(FleetGoalKind.Contract, "Deliver iron", 100, ContractId: "c1");

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Construction goal
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_Construction_ResolvesAndActivatesGoal()
    {
        var ship = MakeShip();
        _ships.FindAsync("SHIP-1", Arg.Any<CancellationToken>()).Returns(ship);

        var resolvedGoal = new SupplyConstructionGoal { TradeSymbol = "ALUMINUM", ConstructionSiteWaypointSymbol = "X1-AB-CONST" };
        _resolver
            .ResolveAsync(ship, Arg.Any<ResourceProductionGoal>(), Arg.Any<CancellationToken>())
            .Returns(resolvedGoal);

        var fleetGoal = new FleetGoal(
            FleetGoalKind.Construction,
            "Supply aluminum",
            Priority: 80,
            TradeSymbol: "ALUMINUM",
            DestinationWaypoint: "X1-AB-CONST",
            RemainingUnits: 100);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _resolver.Received(1).ResolveAsync(
            ship,
            Arg.Is<ResourceProductionGoal>(g =>
                g.TradeSymbol == "ALUMINUM" &&
                g.PurposeKind == ResourceProductionPurpose.Construction &&
                g.PurposeId == "X1-AB-CONST" &&
                g.UnitsNeeded == 100),
            Arg.Any<CancellationToken>());

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", resolvedGoal, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1"),
            Arg.Any<DeliveryOptions>());
    }

    // ────────────────────────────────────────────────────────────────────────────
    // MarketCoverage goal
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MarketCoverage_UsesResolverAndActivatesGoal()
    {
        var patrolGoal = new PatrolMarketGoal { TargetWaypointSymbol = "X1-AB-MKT" };
        _resolver
            .ResolveMarketCoverageAsync("X1-AB-MKT", Arg.Any<CancellationToken>())
            .Returns(patrolGoal);

        var fleetGoal = new FleetGoal(
            FleetGoalKind.MarketCoverage,
            "Cover market",
            Priority: 40,
            OriginWaypoint: "X1-AB-MKT");

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _resolver.Received(1).ResolveMarketCoverageAsync("X1-AB-MKT", Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", patrolGoal, Arg.Any<CancellationToken>());
        await _bus.Received(1).PublishAsync(
            Arg.Is<ShipAutomationTickEvent>(e => e.ShipSymbol == "SHIP-1" && e.Reason == "GoalAssigned"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_MarketCoverage_WhenNoOriginWaypoint_DoesNotSetGoal()
    {
        var fleetGoal = new FleetGoal(FleetGoalKind.MarketCoverage, "Cover market", 40);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
        await _bus.DidNotReceive().PublishAsync(Arg.Any<ShipAutomationTickEvent>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_MarketCoverage_ScoutGoal_WhenResolverReturnsScout()
    {
        var scoutGoal = new ScoutWaypointGoal { TargetWaypointSymbol = "X1-AB-UNKNOWN" };
        _resolver
            .ResolveMarketCoverageAsync("X1-AB-UNKNOWN", Arg.Any<CancellationToken>())
            .Returns(scoutGoal);

        var fleetGoal = new FleetGoal(
            FleetGoalKind.MarketCoverage,
            "Scout market",
            Priority: 40,
            OriginWaypoint: "X1-AB-UNKNOWN");

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.Received(1).SetActiveGoalAsync("SHIP-1", scoutGoal, Arg.Any<CancellationToken>());
    }

    // ────────────────────────────────────────────────────────────────────────────
    // FleetExpansion goal
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_FleetExpansion_EmitsPurchaseShipCommand()
    {
        _settings
            .GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>())
            .Returns("SHIP_MINING_DRONE");
        _shipyards
            .FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns("X1-AB-SHIPYARD");

        var fleetGoal = new FleetGoal(FleetGoalKind.FleetExpansion, "Expand fleet", 30, EstimatedCost: 200_000);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _bus.Received(1).SendAsync(
            Arg.Is<PurchaseShipCommand>(c =>
                c.ShipType == "SHIP_MINING_DRONE" &&
                c.ShipyardWaypoint == "X1-AB-SHIPYARD"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_FleetExpansion_WhenNoPreferredShipType_DoesNotPurchase()
    {
        _settings
            .GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>())
            .Returns(string.Empty);

        var fleetGoal = new FleetGoal(FleetGoalKind.FleetExpansion, "Expand fleet", 30);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _bus.DidNotReceive().SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_FleetExpansion_WhenNoShipyardFound_DoesNotPurchase()
    {
        _settings
            .GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>())
            .Returns("SHIP_MINING_DRONE");
        _shipyards
            .FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var fleetGoal = new FleetGoal(FleetGoalKind.FleetExpansion, "Expand fleet", 30);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _bus.DidNotReceive().SendAsync(Arg.Any<PurchaseShipCommand>(), Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task Handle_FleetExpansion_DoesNotSetGoalOnAnyShip()
    {
        _settings
            .GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>())
            .Returns("SHIP_MINING_DRONE");
        _shipyards
            .FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>())
            .Returns("X1-AB-SHIPYARD");

        var fleetGoal = new FleetGoal(FleetGoalKind.FleetExpansion, "Expand fleet", 30);

        await CreateHandler().Handle(new AssignShipToGoalCommand("SHIP-1", fleetGoal), CancellationToken.None);

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }
}

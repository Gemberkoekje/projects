using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Orchestration;

public sealed class FleetGoalEvaluatorTests
{
    private static ITradeAnalyser CreateTradeAnalyser() => new TradeAnalyser();

    [Fact]
    public async Task ContractGoalEvaluator_EmitsGoalForUnfulfilledDeliverables()
    {
        var contracts = Substitute.For<IContractRepository>();
        var deliverables = new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-DEST", 100, 40),
            new ContractDeliverableDto("COPPER_ORE", "X1-AB-DEST", 50, 50),
        };
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto("c1", "COSMIC", "PROCURE", true, false, null, null,
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(2),
                DeliverablesJson: JsonSerializer.Serialize(deliverables)),
        ]);

        var evaluator = new ContractGoalEvaluator(contracts);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Kind.Should().Be(FleetGoalKind.Contract);
        goals[0].TradeSymbol.Should().Be("IRON_ORE");
        goals[0].RemainingUnits.Should().Be(60);
        goals[0].ContractId.Should().Be("c1");
    }

    [Fact]
    public async Task ContractGoalEvaluator_SkipsFulfilledOrUnaccepted()
    {
        var contracts = Substitute.For<IContractRepository>();
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto("c1", "COSMIC", "PROCURE", false, false, null, null),
            new ContractDto("c2", "COSMIC", "PROCURE", true, true, null, null),
        ]);

        var evaluator = new ContractGoalEvaluator(contracts);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task ConstructionGoalEvaluator_EmitsGoalPerIncompleteMaterial()
    {
        var constructions = Substitute.For<IConstructionRepository>();
        constructions.GetIncompleteAsync(Arg.Any<CancellationToken>()).Returns([
            new ConstructionSiteModel("X1-AB-GATE", false, [
                new ConstructionMaterialModel("FAB_MATS", 1000, 200),
                new ConstructionMaterialModel("ADVANCED_CIRCUITRY", 500, 500),
            ]),
        ]);
        var markets = Substitute.For<IMarketRepository>();
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new ConstructionGoalEvaluator(constructions, markets, CreateTradeAnalyser());
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().ContainSingle(g =>
            g.Kind == FleetGoalKind.Construction
            && g.TradeSymbol == "FAB_MATS"
            && g.RemainingUnits == 800
            && g.DestinationWaypoint == "X1-AB-GATE"
            && !g.IsSupplyChainPrecursor);
    }

    [Fact]
    public async Task MarketCoverageGoalEvaluator_SkipsCoveredMarkets()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("S1", "X1-AB", "X1-AB-1", "DOCKED", "CRUISE", 100, 100),
        ]);
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT-1", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-MKT-2", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var now = DateTimeOffset.UtcNow;
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("PROBE-1", "MarketProbe", "X1-AB-MKT-1", null, null, null, 0, now, null),
        ]);

        var evaluator = new MarketCoverageGoalEvaluator(ships, waypoints, assignments);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].OriginWaypoint.Should().Be("X1-AB-MKT-2");
    }

    [Fact]
    public async Task ContractGoalEvaluator_EmitsCorrectDefaultPriority()
    {
        var contracts = Substitute.For<IContractRepository>();
        var deliverables = new[]
        {
            new ContractDeliverableDto("IRON_ORE", "X1-AB-DEST", 100, 0),
        };
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ContractDto("c1", "COSMIC", "PROCURE", true, false, null, null,
                TermsDeadline: DateTimeOffset.UtcNow.AddDays(2),
                DeliverablesJson: JsonSerializer.Serialize(deliverables)),
        ]);

        var evaluator = new ContractGoalEvaluator(contracts);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Priority.Should().Be(FleetGoalPriority.Contract);
    }

    [Fact]
    public async Task ConstructionGoalEvaluator_EmitsCorrectDefaultPriority()
    {
        var constructions = Substitute.For<IConstructionRepository>();
        constructions.GetIncompleteAsync(Arg.Any<CancellationToken>()).Returns([
            new ConstructionSiteModel("X1-AB-GATE", false, [
                new ConstructionMaterialModel("FAB_MATS", 1000, 0),
            ]),
        ]);
        var markets = Substitute.For<IMarketRepository>();
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new ConstructionGoalEvaluator(constructions, markets, CreateTradeAnalyser());
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().Contain(g => g.Priority == FleetGoalPriority.Construction && !g.IsSupplyChainPrecursor);
    }

    [Fact]
    public async Task ConstructionGoalEvaluator_EmitsPrecursorGoalsForConstrainedDependencies()
    {
        var constructions = Substitute.For<IConstructionRepository>();
        constructions.GetIncompleteAsync(Arg.Any<CancellationToken>()).Returns([
            new ConstructionSiteModel("X1-AB-GATE", false, [
                new ConstructionMaterialModel("FAB_MATS", 1000, 0),
            ]),
        ]);
        var markets = Substitute.For<IMarketRepository>();
        markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns([
            new MarketSnapshot(
                "X1-AB-MKT-IRON",
                "X1-AB",
                [new TradeGoodSnapshot("IRON", "EXPORT", 100, 120, 50, "LIMITED")],
                [],
                ["IRON"],
                []),
        ]);

        var evaluator = new ConstructionGoalEvaluator(constructions, markets, CreateTradeAnalyser());
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().Contain(g =>
            g.IsSupplyChainPrecursor
            && g.TradeSymbol == "IRON"
            && g.Priority == FleetGoalPriority.ConstructionPrecursor
            && g.OriginWaypoint == "X1-AB-MKT-IRON");
    }

    [Fact]
    public async Task MarketCoverageGoalEvaluator_EmitsCorrectDefaultPriority()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("S1", "X1-AB", "X1-AB-1", "DOCKED", "CRUISE", 100, 100),
        ]);
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT-1", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new MarketCoverageGoalEvaluator(ships, waypoints, assignments);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Priority.Should().Be(FleetGoalPriority.MarketCoverage);
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_EmitsCorrectDefaultPriority()
    {
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 1, 1, 0, 0, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_PROBE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Priority.Should().Be(FleetGoalPriority.FleetExpansion);
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_SkipsWhenIdleShipsExist()
    {
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 0, 0, 0, 0, IdleShips: 1, TotalCargoCapacity: 0, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        var settings = Substitute.For<ISettingsRepository>();
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 1_000_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_EmitsGoalWhenAffordableAndNoIdle()
    {
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        // 1 mining, 1 hauling, 0 scouting → scouting bottleneck → picks SHIP_PROBE
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 1, 1, 0, 0, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_PROBE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Kind.Should().Be(FleetGoalKind.FleetExpansion);
        goals[0].EstimatedCost.Should().Be(400_000);
        goals[0].ExpansionShipType.Should().Be("SHIP_PROBE");
        goals[0].ExpansionShipyardWaypoint.Should().Be("X1-AB-SHIPYARD");
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_SkipsAtFleetCap()
    {
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        var budget = Substitute.For<IBudgetPolicy>();
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(2);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_BottleneckExtraction_SelectsMiningDrone()
    {
        // 0 mining, 1 hauling, 1 scouting, 0 idle → mining bottleneck → SHIP_MINING_DRONE
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 0, 1, 0, 1, IdleShips: 0, TotalCargoCapacity: 40, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].ExpansionShipType.Should().Be("SHIP_MINING_DRONE");
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_BottleneckCargo_SelectsLightHauler()
    {
        // 2 mining, 0 hauling, 1 scouting, 0 idle → cargo bottleneck → SHIP_LIGHT_HAULER
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(3, 2, 0, 0, 1, IdleShips: 0, TotalCargoCapacity: 40, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 3));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_LIGHT_HAULER", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].ExpansionShipType.Should().Be("SHIP_LIGHT_HAULER");
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_BottleneckMarketCoverage_SelectsProbe()
    {
        // 1 mining, 1 hauling, 0 scouting, 0 idle → market coverage bottleneck → SHIP_PROBE
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 1, 1, 0, 0, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_PROBE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].ExpansionShipType.Should().Be("SHIP_PROBE");
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_FallsBackToSettings_WhenNoBottleneck()
    {
        // 1 mining, 1 hauling, 1 scouting, 0 idle → no specific bottleneck → falls back to settings
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(3, 1, 1, 0, 1, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        settings.GetAsync<string>("FleetExpansion.PreferredShipType", Arg.Any<CancellationToken>()).Returns("SHIP_MINING_DRONE");
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 3));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync("SHIP_MINING_DRONE", Arg.Any<CancellationToken>()).Returns("X1-AB-SHIPYARD");

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].ExpansionShipType.Should().Be("SHIP_MINING_DRONE");
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_SkipsWhenNoShipyardFound()
    {
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 1, 1, 0, 0, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(true, 500_000, 100_000, 400_000));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 500_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();
        shipyards.FindShipyardForTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task FleetExpansionGoalEvaluator_SkipsWhenBudgetInsufficient()
    {
        // Budget returns CanAfford=false → fleet expansion evaluator produces no goals.
        var capacity = Substitute.For<IFleetCapacityEstimator>();
        capacity.EstimateAsync(Arg.Any<CancellationToken>()).Returns(
            new FleetCapacityEstimate(2, 1, 1, 0, 0, IdleShips: 0, TotalCargoCapacity: 60, IdleCargoCapacity: 0));
        var budget = Substitute.For<IBudgetPolicy>();
        budget.EvaluateAsync(0, Arg.Any<CancellationToken>()).Returns(
            new BudgetDecision(false, 50_000, 100_000, 0,
                Reason: "Proposed cost exceeds spendable credits"));
        var settings = Substitute.For<ISettingsRepository>();
        settings.GetAsync<int>("FleetExpansion.MaxShips", Arg.Any<CancellationToken>()).Returns(10);
        var agents = Substitute.For<IAgentRepository>();
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(new AgentModel("A", null, null, 50_000, "COSMIC", 2));
        var shipyards = Substitute.For<IShipyardRepository>();

        var evaluator = new FleetExpansionGoalEvaluator(capacity, budget, settings, agents, shipyards);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task MarketCoverageGoalEvaluator_AllMarketsCovered_ProducesNoGoal()
    {
        // All market waypoints already have active MarketProbe assignments → evaluator returns empty.
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("S1", "X1-AB", "X1-AB-1", "DOCKED", "CRUISE", 100, 100),
        ]);
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT-1", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-MKT-2", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        var now = DateTimeOffset.UtcNow;
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipAssignmentDto("PROBE-1", "MarketProbe", "X1-AB-MKT-1", null, null, null, 0, now, null),
            new ShipAssignmentDto("PROBE-2", "MarketProbe", "X1-AB-MKT-2", null, null, null, 0, now, null),
        ]);

        var evaluator = new MarketCoverageGoalEvaluator(ships, waypoints, assignments);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // MarketScoutingGoalEvaluator
    // ────────────────────────────────────────────────────────────────────────────

    private static (IWaypointRepository waypoints, IMarketRepository markets, IShipGoalRepository shipGoals)
        MakeMarketScoutingDeps()
    {
        return (
            Substitute.For<IWaypointRepository>(),
            Substitute.For<IMarketRepository>(),
            Substitute.For<IShipGoalRepository>());
    }

    [Fact]
    public async Task MarketScoutingGoalEvaluator_MarketWithNoCachedData_ProducesGoal()
    {
        var (waypoints, markets, shipGoals) = MakeMarketScoutingDeps();
        waypoints.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>()).Returns(["X1-AB"]);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns([]);
        shipGoals.GetActiveScoutTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var evaluator = new MarketScoutingGoalEvaluator(waypoints, markets, shipGoals);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(1);
        goals[0].Kind.Should().Be(FleetGoalKind.MarketScouting);
        goals[0].OriginWaypoint.Should().Be("X1-AB-MKT");
        goals[0].Priority.Should().Be(FleetGoalPriority.MarketScouting);
    }

    [Fact]
    public async Task MarketScoutingGoalEvaluator_MarketWithRecentData_ProducesNoGoal()
    {
        var (waypoints, markets, shipGoals) = MakeMarketScoutingDeps();
        waypoints.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>()).Returns(["X1-AB"]);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns([
            new MarketFreshnessRecord("X1-AB-MKT", "X1-AB", DateTimeOffset.UtcNow.AddSeconds(-30)),
        ]);
        shipGoals.GetActiveScoutTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var evaluator = new MarketScoutingGoalEvaluator(waypoints, markets, shipGoals);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task MarketScoutingGoalEvaluator_MarketAlreadyHasScout_ProducesNoGoal()
    {
        var (waypoints, markets, shipGoals) = MakeMarketScoutingDeps();
        waypoints.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>()).Returns(["X1-AB"]);
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-MKT", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow),
        ]);
        markets.GetAllFreshnessAsync(Arg.Any<CancellationToken>()).Returns([]);
        shipGoals.GetActiveScoutTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(["X1-AB-MKT"], StringComparer.OrdinalIgnoreCase));

        var evaluator = new MarketScoutingGoalEvaluator(waypoints, markets, shipGoals);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().BeEmpty();
    }

    [Fact]
    public async Task MarketCoverageGoalEvaluator_PrioritizesIndustrialAndHighTechMarkets()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([
            new ShipModel("S1", "X1-AB", "X1-AB-1", "DOCKED", "CRUISE", 100, 100),
        ]);
        var waypoints = Substitute.For<IWaypointRepository>();
        waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>()).Returns([
            new WaypointCacheModel("X1-AB-ORBIT", "X1-AB", "ORBITAL_STATION", 0, 0, true, false, DateTimeOffset.UtcNow),
            new WaypointCacheModel("X1-AB-HT", "X1-AB", "ORBITAL_STATION", 0, 0, true, false, DateTimeOffset.UtcNow, TraitsJson: "HIGH_TECH"),
            new WaypointCacheModel("X1-AB-IND", "X1-AB", "PLANET", 0, 0, true, false, DateTimeOffset.UtcNow, TraitsJson: "INDUSTRIAL"),
        ]);
        var assignments = Substitute.For<IShipAssignmentRepository>();
        assignments.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        var evaluator = new MarketCoverageGoalEvaluator(ships, waypoints, assignments);
        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().HaveCount(3);
        goals[0].OriginWaypoint.Should().Be("X1-AB-IND");
        goals[1].OriginWaypoint.Should().Be("X1-AB-HT");
    }
}

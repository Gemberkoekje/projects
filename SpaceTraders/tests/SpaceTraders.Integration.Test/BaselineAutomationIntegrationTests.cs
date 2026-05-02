using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Fleet;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Sync;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Integration.Test;

/// <summary>
/// Baseline integration-style tests that pin current automation behavior against a captured startup snapshot.
/// </summary>
public sealed class BaselineAutomationIntegrationTests
{
    private const string SnapshotFileName = "startup-snapshot-6-20260501-223730-initial.json";
    private const string SystemSymbol = "X1-BQ60";
    private const string CommandShipSymbol = "SPECTERDEBUG3-1";
    private const string SatelliteShipSymbol = "SPECTERDEBUG3-2";
    private const string MiningDroneType = "SHIP_MINING_DRONE";
    private const string SurveyorType = "SHIP_SURVEYOR";
    private static readonly DateTimeOffset BaselineObservedAt = new(2026, 5, 1, 22, 37, 30, TimeSpan.Zero);

    [Fact]
    public async Task MarketCoverageGoalEvaluator_RequestsCoverageForMostKnownMarkets_FromBaseline()
    {
        var snapshot = TestSnapshot.Load();
        var ships = CreateShipRepository(snapshot.CreateShipModels());
        var waypoints = CreateWaypointRepository(snapshot.CreateWaypointModels());
        var assignments = CreateAssignmentRepository(Array.Empty<ShipAssignmentDto>());
        var evaluator = new MarketCoverageGoalEvaluator(ships, waypoints, assignments);

        var goals = await evaluator.EvaluateAsync(CancellationToken.None);

        goals.Should().NotBeEmpty();
        goals.Should().OnlyContain(goal => goal.Kind == FleetGoalKind.MarketCoverage);
        goals.Select(goal => goal.OriginWaypoint).Should().BeEquivalentTo(snapshot.MarketWaypointSymbols);
        goals.Should().Contain(goal => goal.OriginWaypoint == "X1-BQ60-A1");
        goals.Should().Contain(goal => goal.OriginWaypoint == "X1-BQ60-H62");
    }

    [Fact]
    public async Task FleetOrchestrator_AssignsAvailableScoutToFirstUncoveredMarket_FromBaseline()
    {
        var snapshot = TestSnapshot.Load();
        var bus = Substitute.For<IMessageBus>();
        var ships = CreateShipRepository(snapshot.CreateShipModels());
        var goal = new FleetGoal(FleetGoalKind.MarketCoverage, "Baseline market coverage", 40, OriginWaypoint: "X1-BQ60-A1");
        var orchestrator = new FleetOrchestrator(
            new[] { CreateEvaluator(goal) },
            ships,
            CreateEmptyShipGoalRepository(),
            CreateEmptyFleetGoalRepository(),
            bus,
            NullLogger<FleetOrchestrator>.Instance);

        await orchestrator.EvaluateAndAssignAsync(CancellationToken.None);

        await bus.Received(1).SendAsync(
            Arg.Is<AssignShipToGoalCommand>(command =>
                command.ShipSymbol == CommandShipSymbol
                && command.Goal.Kind == FleetGoalKind.MarketCoverage
                && command.Goal.OriginWaypoint == "X1-BQ60-A1"),
            Arg.Any<DeliveryOptions>());
    }

    [Fact]
    public async Task ContractObjectivePlanner_UsesKnownMarketData_ToSourceContractCargo_FromBaseline()
    {
        var snapshot = TestSnapshot.Load();
        var contracts = Substitute.For<IContractRepository>();
        var markets = CreateMarketRepository(snapshot.CreateMarketSnapshots());
        var planner = new ContractObjectivePlanner(contracts, markets);
        var ship = snapshot.CreateShipModels().Single(model => model.Symbol == CommandShipSymbol);
        var contract = new ContractDto(
            "contract-1",
            "COBALT",
            "PROCUREMENT",
            true,
            false,
            BaselineObservedAt.AddDays(1),
            BaselineObservedAt.AddHours(12),
            BaselineObservedAt.AddDays(2),
            "[{\"TradeSymbol\":\"FOOD\",\"DestinationSymbol\":\"X1-BQ60-D47\",\"UnitsRequired\":30,\"UnitsFulfilled\":0}]");
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { contract });

        var assignment = await planner.PlanAsync(ship, CancellationToken.None);

        assignment.Should().NotBeNull();
        assignment.AssignmentType.Should().Be("Contract");
        assignment.OriginWaypoint.Should().Be("X1-BQ60-A1");
        assignment.DestWaypoint.Should().Be("X1-BQ60-D47");
        assignment.CargoSymbol.Should().Be("FOOD");
        assignment.RequiredUnits.Should().Be(30);
    }

    [Fact]
    public async Task ShipAssignmentPlanner_CurrentlyPrefersMiningForCommandShip_EvenWhenTradeRouteExists_FromBaseline()
    {
        var snapshot = TestSnapshot.Load();
        var tradeOpportunities = Substitute.For<ITradeOpportunityRepository>();
        var settings = CreateSettingsRepository(new Dictionary<string, object>
        {
            ["Trade.MinProfitPerUnit"] = 10,
            ["Trade.MaxHaulDistance"] = 200,
            ["Automation.MiningShipPercentage"] = 0.25m,
            ["Scout.MarketRefreshIntervalMinutes"] = 30,
        });
        var assignments = CreateAssignmentRepository(Array.Empty<ShipAssignmentDto>());
        var waypoints = CreateWaypointRepository(snapshot.CreateWaypointModels());
        var ships = CreateShipRepository(snapshot.CreateShipModels());
        var markets = CreateMarketRepository(snapshot.CreateMarketSnapshots());
        var contractObjectives = Substitute.For<IContractObjectivePlanner>();
        contractObjectives.PlanAsync(Arg.Any<ShipModel>(), Arg.Any<CancellationToken>()).Returns((AssignShipCommand)null!);
        var route = new TradeOpportunityDto(
            1,
            "FOOD",
            "X1-BQ60-A1",
            "X1-BQ60-D47",
            4362,
            9000,
            4638,
            2,
            2319m,
            false,
            0,
            BaselineObservedAt);
        tradeOpportunities.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(route);
        tradeOpportunities.GetTopRoutesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TradeOpportunityDto(
                    2,
                    "IRON_ORE",
                    "X1-BQ60-B9",
                    "X1-BQ60-H62",
                    100,
                    200,
                    100,
                    1,
                    100m,
                    false,
                    0,
                    BaselineObservedAt),
            });

        var planner = new ShipAssignmentPlanner(
            contractObjectives,
            tradeOpportunities,
            settings,
            assignments,
            waypoints,
            ships,
            markets);

        var assignment = await planner.PlanAsync(snapshot.CreateShipModels().Single(model => model.Symbol == CommandShipSymbol), CancellationToken.None);

        assignment.AssignmentType.Should().Be("Mine");
        assignment.CargoSymbol.Should().Be("IRON_ORE");
        assignment.OriginWaypoint.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ShipAssignmentPlanner_UsesBestKnownSellCandidate_ForMining_FromBaseline()
    {
        var snapshot = TestSnapshot.Load();
        var tradeOpportunities = Substitute.For<ITradeOpportunityRepository>();
        var settings = CreateSettingsRepository(new Dictionary<string, object>
        {
            ["Trade.MinProfitPerUnit"] = 10,
            ["Trade.MaxHaulDistance"] = 200,
            ["Automation.MiningShipPercentage"] = 1m,
            ["Scout.MarketRefreshIntervalMinutes"] = 30,
        });
        var assignments = CreateAssignmentRepository(Array.Empty<ShipAssignmentDto>());
        var waypoints = CreateWaypointRepository(snapshot.CreateWaypointModels());
        var ships = CreateShipRepository(snapshot.CreateShipModels());
        var markets = CreateMarketRepository(snapshot.CreateMarketSnapshots());
        var contractObjectives = Substitute.For<IContractObjectivePlanner>();
        contractObjectives.PlanAsync(Arg.Any<ShipModel>(), Arg.Any<CancellationToken>()).Returns((AssignShipCommand)null!);
        tradeOpportunities.GetBestRouteForCapacityAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((TradeOpportunityDto)null!);
        tradeOpportunities.GetTopRoutesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new TradeOpportunityDto(
                    2,
                    "IRON_ORE",
                    "X1-BQ60-B9",
                    "X1-BQ60-H62",
                    100,
                    200,
                    100,
                    1,
                    100m,
                    false,
                    0,
                    BaselineObservedAt),
            });

        var planner = new ShipAssignmentPlanner(
            contractObjectives,
            tradeOpportunities,
            settings,
            assignments,
            waypoints,
            ships,
            markets);

        var assignment = await planner.PlanAsync(snapshot.CreateShipModels().Single(model => model.Symbol == CommandShipSymbol), CancellationToken.None);

        assignment.AssignmentType.Should().Be("Mine");
        assignment.OriginWaypoint.Should().NotBeNullOrWhiteSpace();
        assignment.OriginWaypoint.Should().StartWith("X1-BQ60-B");
        assignment.DestWaypoint.Should().Be("X1-BQ60-DB5E");
        assignment.CargoSymbol.Should().Be("IRON_ORE");
    }

    [Fact]
    public async Task Phase3OnboardingHandler_CurrentlyAssignsContractWithoutBuyingMiningDrone_WhenCommandShipAlreadyHasMiningEquipment()
    {
        var snapshot = TestSnapshot.Load();
        var bus = Substitute.For<IMessageBus>();
        var agents = Substitute.For<IAgentRepository>();
        var contracts = Substitute.For<IContractRepository>();
        var ships = CreateShipRepository(snapshot.CreateShipModels());
        var waypoints = CreateWaypointRepository(snapshot.CreateWaypointModels());
        var shipyards = CreateShipyardRepository(new Dictionary<string, string>
        {
            [MiningDroneType] = "X1-BQ60-H62",
            [SurveyorType] = "X1-BQ60-H62",
        });
        agents.GetAsync(Arg.Any<CancellationToken>()).Returns(snapshot.CreateAgentModel());

        var contract = new ContractDto(
            "contract-1",
            "COBALT",
            "PROCUREMENT",
            false,
            false,
            BaselineObservedAt.AddDays(3),
            BaselineObservedAt.AddHours(6),
            BaselineObservedAt.AddDays(5),
            "[{\"TradeSymbol\":\"FOOD\",\"DestinationSymbol\":\"X1-BQ60-D47\",\"UnitsRequired\":30,\"UnitsFulfilled\":0}]");
        contracts.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(new[] { contract });
        contracts.FindAsync(contract.Id, Arg.Any<CancellationToken>()).Returns(contract with { IsAccepted = true });

        var handler = new Phase3OnboardingHandler(
            agents,
            contracts,
            ships,
            waypoints,
            shipyards,
            bus,
            NullLogger<Phase3OnboardingHandler>.Instance);

        await handler.Handle(new RunPhase3OnboardingCommand(), CancellationToken.None);

        await bus.Received().InvokeAsync(
            Arg.Is<RefreshSystemDataCommand>(command => command.SystemSymbol == SystemSymbol && command.ForceRefresh),
            Arg.Any<CancellationToken>());
        await bus.Received().InvokeAsync(
            Arg.Is<AcceptContractCommand>(command => command.ContractId == contract.Id),
            Arg.Any<CancellationToken>());
        await bus.DidNotReceive().InvokeAsync(
            Arg.Any<PurchaseShipCommand>(),
            Arg.Any<CancellationToken>());
        await bus.Received().InvokeAsync(
            Arg.Is<AssignShipToGoalCommand>(command =>
                command.ShipSymbol == CommandShipSymbol
                && command.Goal.Kind == FleetGoalKind.Contract
                && command.Goal.ContractId == "contract-1"
                && command.Goal.TradeSymbol == "FOOD"
                && command.Goal.RemainingUnits == 30),
            Arg.Any<CancellationToken>());
    }

    private static IFleetGoalEvaluator CreateEvaluator(params FleetGoal[] goals)
    {
        var evaluator = Substitute.For<IFleetGoalEvaluator>();
        evaluator.EvaluateAsync(Arg.Any<CancellationToken>()).Returns(goals);
        return evaluator;
    }

    private static IFleetGoalRepository CreateEmptyFleetGoalRepository()
    {
        var repo = Substitute.For<IFleetGoalRepository>();
        repo.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repo;
    }

    private static IShipGoalRepository CreateEmptyShipGoalRepository()
    {
        var repo = Substitute.For<IShipGoalRepository>();
        repo.GetActiveGoalAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((ShipGoal?)null);
        return repo;
    }

    private static IShipRepository CreateShipRepository(IReadOnlyList<ShipModel> fleet)
    {
        var repository = Substitute.For<IShipRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);
        repository.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => fleet.FirstOrDefault(ship => ship.Symbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase)));
        repository.IsShipAtWaypointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => fleet.Any(ship => ship.WaypointSymbol != null && ship.WaypointSymbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase)));
        return repository;
    }

    private static IWaypointRepository CreateWaypointRepository(IReadOnlyList<WaypointCacheModel> waypoints)
    {
        var repository = Substitute.For<IWaypointRepository>();
        repository.GetBySystemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<WaypointCacheModel>)waypoints
                .Where(waypoint => waypoint.SystemSymbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase))
                .ToArray());
        repository.GetUnscoutedOrStaleAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var systemSymbol = (string)call[0];
                var staleness = (TimeSpan)call[1];
                var staleThreshold = BaselineObservedAt - staleness;
                return (IReadOnlyList<WaypointCacheModel>)waypoints
                    .Where(waypoint => waypoint.SystemSymbol.Equals(systemSymbol, StringComparison.OrdinalIgnoreCase))
                    .Where(waypoint => waypoint.LastObservedAt <= staleThreshold)
                    .ToArray();
            });
        repository.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => waypoints.FirstOrDefault(waypoint => waypoint.Symbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase)));
        repository.GetVisitedSystemSymbolsAsync(Arg.Any<CancellationToken>())
            .Returns(waypoints.Select(waypoint => waypoint.SystemSymbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        return repository;
    }

    private static IMarketRepository CreateMarketRepository(IReadOnlyList<MarketSnapshot> snapshots)
    {
        var repository = Substitute.For<IMarketRepository>();
        repository.GetAllSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(snapshots);
        repository.FindSnapshotByWaypointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => snapshots.FirstOrDefault(snapshot => snapshot.WaypointSymbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase)));
        repository.GetAllFreshnessAsync(Arg.Any<CancellationToken>())
            .Returns(snapshots.Select(snapshot => new MarketFreshnessRecord(snapshot.WaypointSymbol, snapshot.SystemSymbol, BaselineObservedAt)).ToArray());
        repository.GetLastObservedAtAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var match = snapshots.FirstOrDefault(snapshot => snapshot.WaypointSymbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase));
                return match is null ? (DateTimeOffset?)null : BaselineObservedAt;
            });
        return repository;
    }

    private static IShipAssignmentRepository CreateAssignmentRepository(IReadOnlyList<ShipAssignmentDto> activeAssignments)
    {
        var repository = Substitute.For<IShipAssignmentRepository>();
        repository.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(activeAssignments);
        repository.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => activeAssignments.FirstOrDefault(assignment => assignment.ShipSymbol.Equals((string)call[0], StringComparison.OrdinalIgnoreCase)));
        return repository;
    }

    private static ISettingsRepository CreateSettingsRepository(IReadOnlyDictionary<string, object> values)
    {
        var repository = Substitute.For<ISettingsRepository>();
        repository.GetAsync<int>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => values.TryGetValue((string)call[0], out var value) ? Convert.ToInt32(value) : 0);
        repository.GetAsync<decimal>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => values.TryGetValue((string)call[0], out var value) ? Convert.ToDecimal(value) : 0m);
        repository.GetAsync<long>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => values.TryGetValue((string)call[0], out var value) ? Convert.ToInt64(value) : 0L);
        repository.GetAsync<bool>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => values.TryGetValue((string)call[0], out var value) && Convert.ToBoolean(value));
        repository.GetRawAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => values.TryGetValue((string)call[0], out var value) ? Convert.ToString(value) : null);
        return repository;
    }

    private static IShipyardRepository CreateShipyardRepository(IReadOnlyDictionary<string, string> shipyardsByType)
    {
        var repository = Substitute.For<IShipyardRepository>();
        repository.FindShipyardForTypeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => shipyardsByType.TryGetValue((string)call[0], out var waypoint) ? waypoint : null);
        return repository;
    }

    private sealed class TestSnapshot
    {
        public required SnapshotAgent Agent { get; init; }

        public required IReadOnlyList<SnapshotShip> Ships { get; init; }

        public required IReadOnlyList<SnapshotSystem> Systems { get; init; }

        public IReadOnlyList<string> MarketWaypointSymbols => Systems
            .SelectMany(system => system.Waypoints)
            .Where(waypoint => waypoint.Waypoint.Traits.Any(trait => trait.Symbol == "MARKETPLACE"))
            .Select(waypoint => waypoint.Waypoint.Symbol)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        public static TestSnapshot Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, SnapshotFileName);
            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<TestSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                throw new InvalidOperationException($"Could not deserialize baseline snapshot '{SnapshotFileName}'.");
            }

            return snapshot;
        }

        public AgentModel CreateAgentModel() => new(
            Agent.Symbol,
            Agent.AccountId,
            Agent.Headquarters,
            Agent.Credits,
            Agent.StartingFaction,
            Agent.ShipCount);

        public IReadOnlyList<ShipModel> CreateShipModels() => Ships.Select(ship =>
            new ShipModel(
                ship.Symbol,
                ship.Nav.SystemSymbol,
                ship.Nav.WaypointSymbol,
                ship.Nav.Status,
                ship.Nav.FlightMode,
                ship.Fuel.Current,
                ship.Fuel.Capacity,
                ship.Nav.Route.Arrival,
                ship.Nav.Route.Destination.Symbol,
                ship.Cargo.Units,
                ship.Cargo.Capacity,
                BaselineObservedAt,
                ResolveShipType(ship),
                ship.Mounts.Select(mount => mount.Symbol).ToArray(),
                ship.Cargo.Inventory.Select(item => new CargoItemModel(item.Symbol, item.Units)).ToArray(),
                JsonSerializer.Serialize(ship.Modules),
                JsonSerializer.Serialize(ship.Frame),
                JsonSerializer.Serialize(ship.Reactor),
                JsonSerializer.Serialize(ship.Engine),
                ship.Cooldown.RemainingSeconds > 0 ? BaselineObservedAt.AddSeconds(ship.Cooldown.RemainingSeconds) : null))
            .ToArray();

        public IReadOnlyList<WaypointCacheModel> CreateWaypointModels() => Systems
            .SelectMany(system => system.Waypoints)
            .Select(waypoint => new WaypointCacheModel(
                waypoint.Waypoint.Symbol,
                waypoint.Waypoint.SystemSymbol,
                waypoint.Waypoint.Type,
                waypoint.Waypoint.X,
                waypoint.Waypoint.Y,
                waypoint.Waypoint.Traits.Any(trait => trait.Symbol == "MARKETPLACE"),
                waypoint.Waypoint.Traits.Any(trait => trait.Symbol == "SHIPYARD"),
                waypoint.Market is null || waypoint.Market.TradeGoods.Count == 0 ? BaselineObservedAt.AddDays(-30) : BaselineObservedAt,
                JsonSerializer.Serialize(waypoint.Waypoint.Traits),
                JsonSerializer.Serialize(waypoint.Waypoint.Modifiers),
                JsonSerializer.Serialize(waypoint.Waypoint.Orbitals),
                waypoint.Waypoint.Orbits,
                waypoint.Waypoint.IsUnderConstruction,
                waypoint.Waypoint.Chart is null ? null : JsonSerializer.Serialize(waypoint.Waypoint.Chart)))
            .ToArray();

        public IReadOnlyList<MarketSnapshot> CreateMarketSnapshots() => Systems
            .SelectMany(system => system.Waypoints)
            .Where(waypoint => waypoint.Market is not null)
            .Select(waypoint => new MarketSnapshot(
                waypoint.Waypoint.Symbol,
                waypoint.Waypoint.SystemSymbol,
                waypoint.Market.TradeGoods.Select(good => new TradeGoodSnapshot(
                    good.Symbol,
                    good.Type,
                    good.PurchasePrice,
                    good.SellPrice,
                    good.TradeVolume,
                    good.Supply,
                    good.Activity ?? string.Empty)).ToArray(),
                waypoint.Market.Imports.Select(symbol => symbol.Symbol).ToArray(),
                waypoint.Market.Exports.Select(symbol => symbol.Symbol).ToArray(),
                waypoint.Market.Exchange.Select(symbol => symbol.Symbol).ToArray()))
            .ToArray();

        private static string ResolveShipType(SnapshotShip ship)
        {
            if (ship.Symbol == SatelliteShipSymbol)
            {
                return "SHIP_PROBE";
            }

            if (ship.Registration.Role == "COMMAND")
            {
                return "SHIP_COMMAND_FRIGATE";
            }

            return ship.Registration.Role;
        }
    }

    private sealed class SnapshotAgent
    {
        public required string AccountId { get; init; }

        public required string Symbol { get; init; }

        public required string Headquarters { get; init; }

        public required long Credits { get; init; }

        public required string StartingFaction { get; init; }

        public required int ShipCount { get; init; }
    }

    private sealed class SnapshotShip
    {
        public required string Symbol { get; init; }

        public required SnapshotRegistration Registration { get; init; }

        public required SnapshotNav Nav { get; init; }

        public required SnapshotFuel Fuel { get; init; }

        public required SnapshotCargo Cargo { get; init; }

        public required IReadOnlyList<SnapshotMount> Mounts { get; init; }

        public required IReadOnlyList<object> Modules { get; init; }

        public required object Frame { get; init; }

        public required object Reactor { get; init; }

        public required object Engine { get; init; }

        public required SnapshotCooldown Cooldown { get; init; }
    }

    private sealed class SnapshotRegistration
    {
        public required string Role { get; init; }
    }

    private sealed class SnapshotNav
    {
        public required string SystemSymbol { get; init; }

        public required string WaypointSymbol { get; init; }

        public required string Status { get; init; }

        public required string FlightMode { get; init; }

        public required SnapshotRoute Route { get; init; }
    }

    private sealed class SnapshotRoute
    {
        public required DateTimeOffset Arrival { get; init; }

        public required SnapshotRouteEndpoint Origin { get; init; }

        public required SnapshotRouteEndpoint Destination { get; init; }
    }

    private sealed class SnapshotRouteEndpoint
    {
        public required string Symbol { get; init; }

        public required string SystemSymbol { get; init; }
    }

    private sealed class SnapshotFuel
    {
        public required int Current { get; init; }

        public required int Capacity { get; init; }
    }

    private sealed class SnapshotCargo
    {
        public required int Units { get; init; }

        public required int Capacity { get; init; }

        public required IReadOnlyList<SnapshotCargoItem> Inventory { get; init; }
    }

    private sealed class SnapshotCargoItem
    {
        public required string Symbol { get; init; }

        public required int Units { get; init; }
    }

    private sealed class SnapshotMount
    {
        public required string Symbol { get; init; }
    }

    private sealed class SnapshotCooldown
    {
        public required int RemainingSeconds { get; init; }
    }

    private sealed class SnapshotSystem
    {
        public required IReadOnlyList<SnapshotWaypointEntry> Waypoints { get; init; }
    }

    private sealed class SnapshotWaypointEntry
    {
        public required SnapshotWaypoint Waypoint { get; init; }

        public SnapshotMarket? Market { get; init; }
    }

    private sealed class SnapshotWaypoint
    {
        public required string Symbol { get; init; }

        public required string Type { get; init; }

        public required string SystemSymbol { get; init; }

        public required int X { get; init; }

        public required int Y { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Orbitals { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Traits { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Modifiers { get; init; }

        public SnapshotChart? Chart { get; init; }

        public bool IsUnderConstruction { get; init; }

        public string? Orbits { get; init; }
    }

    private sealed class SnapshotChart
    {
        public required string WaypointSymbol { get; init; }

        public required string SubmittedBy { get; init; }

        public required DateTimeOffset SubmittedOn { get; init; }
    }

    private sealed class SnapshotMarket
    {
        public required string Symbol { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Exports { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Imports { get; init; }

        public required IReadOnlyList<SnapshotSymbolValue> Exchange { get; init; }

        public required IReadOnlyList<SnapshotTradeGood> TradeGoods { get; init; }
    }

    private sealed class SnapshotTradeGood
    {
        public required string Symbol { get; init; }

        public required string Type { get; init; }

        public required int TradeVolume { get; init; }

        public required string Supply { get; init; }

        public string? Activity { get; init; }

        public required int PurchasePrice { get; init; }

        public required int SellPrice { get; init; }
    }

    private sealed class SnapshotSymbolValue
    {
        public required string Symbol { get; init; }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}

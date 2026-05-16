using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class MiningAutomationServiceTests
{
    private static readonly DateTimeOffset Now = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IWaypointRepository _waypoints = Substitute.For<IWaypointRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();

    private MiningAutomationService CreateService() =>
        new(
            _markets,
            _ships,
            _goals,
            _waypoints,
            _shipyards,
            _agents,
            _settings,
            _plans,
            _port,
            _budget,
            NullLogger<MiningAutomationService>.Instance);

    private static MarketSnapshot Snapshot(string waypoint, params TradeGoodSnapshot[] goods) =>
        new(
            waypoint,
            "X1-AB",
            goods,
            imports: goods.Where(g => g.Type.Equals("IMPORT", StringComparison.OrdinalIgnoreCase)).Select(g => g.Symbol).ToList(),
            exports: goods.Where(g => g.Type.Equals("EXPORT", StringComparison.OrdinalIgnoreCase)).Select(g => g.Symbol).ToList(),
            exchange: goods.Where(g => g.Type.Equals("EXCHANGE", StringComparison.OrdinalIgnoreCase)).Select(g => g.Symbol).ToList());

    private static TradeGoodSnapshot Good(string symbol, string type = "IMPORT", string supply = "SCARCE") =>
        new(symbol, type, 0, 0, 10, supply);

    private static ShipModel Miner(string symbol, string waypoint = "X1-AB-HQ", string status = "DOCKED") =>
        new(symbol, "X1-AB", waypoint, status, "CRUISE", 10, 40, ShipType: "SHIP_MINING_DRONE", CargoCapacity: 40, MountSymbols: ["MOUNT_MINING_LASER_I"]);

    private static WaypointCacheModel Waypoint(string symbol, string type = "ASTEROID", int x = 0, int y = 0, string? traitsJson = null) =>
        new(symbol, "X1-AB", type, x, y, false, false, Now, TraitsJson: traitsJson);

    private static ShipyardWaypointDto MiningShipyard(string waypoint = "X1-AB-SY1", long price = 23_000) =>
        new()
        {
            WaypointSymbol = waypoint,
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_MINING_DRONE"],
            Ships =
            [
                new ShipyardShipDto
                {
                    Type = "SHIP_MINING_DRONE",
                    PurchasePrice = price,
                }
            ]
        };

    [Fact]
    public async Task EnsureBootstrappedAsync_AssignsIdleMiner_ForScarceMineralImport()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID", x: 40, y: 40),
                Waypoint("X1-AB-MKT1", "ORBITAL_STATION", x: 0, y: 0),
                Waypoint("X1-AB-AST-CLOSE", "ASTEROID", x: 2, y: 1, traitsJson: "COMMON_METAL_DEPOSITS"),
                Waypoint("X1-AB-AST-FAR", "ASTEROID", x: 20, y: 18, traitsJson: "COMMON_METAL_DEPOSITS"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(g =>
                g.TradeSymbol == "IRON_ORE"
                && g.SellWaypointSymbol == "X1-AB-MKT1"
                && g.SourceWaypointSymbol == "X1-AB-AST-CLOSE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_AssignsIdleMiner_ForScarceMineralExchange()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE", type: "EXCHANGE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID", x: 20, y: 20),
                Waypoint("X1-AB-MKT1", "ORBITAL_STATION", x: 0, y: 0),
                Waypoint("X1-AB-AST-CLOSE", "ASTEROID", x: 1, y: 1, traitsJson: "COMMON_METAL_DEPOSITS"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(g =>
                g.TradeSymbol == "IRON_ORE"
                && g.SellWaypointSymbol == "X1-AB-MKT1"
                && g.SourceWaypointSymbol == "X1-AB-AST-CLOSE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PurchasesMiner_WhenNoIdleMinerAndBudgetAllows()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("COPPER_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);

        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([MiningShipyard()]);
        _budget.EvaluateAsync(23_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(true, 200_000, 100_000, 100_000));

        _port.PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                new AgentModel("AGENT", null, "X1-AB-HQ", 177_000, "FACTION", 2),
                "MINER-NEW",
                new NavModel("DOCKED", "X1-AB", "X1-AB-SY1", "CRUISE", null, null),
                new FuelModel(10, 40),
                new CargoModel(0, 20, []),
                23_000));

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([Waypoint("X1-AB-AST", "ASTEROID")]);

        await CreateService().EnsureBootstrappedAsync();

        await _port.Received(1).PurchaseShipAsync("SHIP_MINING_DRONE", "X1-AB-SY1", Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-NEW",
            Arg.Any<MineAndSellGoal>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotPurchase_WhenIdleMinerExists()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("COPPER_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);
        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([Waypoint("X1-AB-AST", "ASTEROID")]);

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(g => g.TradeSymbol == "COPPER_ORE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNotPurchase_WhenDroneCapReached()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("SILVER_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([Miner("MINER-1")]);
        _goals.GetActiveGoalAsync("MINER-1", Arg.Any<CancellationToken>())
            .Returns(new MineAndSellGoal
            {
                TradeSymbol = "IRON_ORE",
                SourceWaypointSymbol = "X1-AB-AST",
                SellWaypointSymbol = "X1-AB-MKTX",
            });

        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(1);

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ClearsStaleMiningGoal_WhenOpportunityNoLongerScarce()
    {
        var miner = Miner("MINER-STALE");

        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("FOOD", type: "IMPORT", supply: "MODERATE")),
            ]);

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns(new MineAndSellGoal
            {
                TradeSymbol = "IRON_ORE",
                SourceWaypointSymbol = "X1-AB-AST",
                SellWaypointSymbol = "X1-AB-MKT1",
            });

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).ClearActiveGoalAsync("MINER-STALE", Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ReassignsShipAfterClearingStaleGoal_WhenNewScarceOpportunityExists()
    {
        var miner = Miner("MINER-1");

        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT2", Good("COPPER_ORE")),
            ]);

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns(
                new MineAndSellGoal
                {
                    TradeSymbol = "IRON_ORE",
                    SourceWaypointSymbol = "X1-AB-AST",
                    SellWaypointSymbol = "X1-AB-MKT1",
                },
                (ShipGoal?)null);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).ClearActiveGoalAsync("MINER-1", Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(g =>
                g.TradeSymbol == "COPPER_ORE"
                && g.SellWaypointSymbol == "X1-AB-MKT2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_AssignsMiner_WhenExistingGoalIsCompleted()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns(new MineAndSellGoal
            {
                Status = SpaceTraders.Domain.Enums.GoalStatus.Completed,
                TradeSymbol = "COPPER_ORE",
                SourceWaypointSymbol = "X1-AB-AST",
                SellWaypointSymbol = "X1-AB-MKT2",
            });

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-MKT1", "ORBITAL_STATION", x: 0, y: 0),
                Waypoint("X1-AB-AST", "ASTEROID", x: 30, y: 30),
                Waypoint("X1-AB-AST-CLOSE", "ASTEROID", x: 2, y: 2, traitsJson: "COMMON_METAL_DEPOSITS"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(g =>
                g.TradeSymbol == "IRON_ORE"
                && g.SellWaypointSymbol == "X1-AB-MKT1"
                && g.SourceWaypointSymbol == "X1-AB-AST-CLOSE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PersistsPendingQueue_WhenNoMinerCanBeAssigned()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        _settings.GetAsync<int>("Mining.MaxDrones", Arg.Any<CancellationToken>()).Returns(20);
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([MiningShipyard()]);
        _budget.EvaluateAsync(23_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(false, 10_000, 100_000, -90_000, "reserve"));

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.MiningAutomation,
            Arg.Is<MiningAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].TradeSymbol == "IRON_ORE"
                && state.Opportunities[0].SellWaypointSymbol == "X1-AB-MKT1"
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PersistsAssignedQueue_WhenMinerIsAssigned()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.MiningAutomation,
            Arg.Is<MiningAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].TradeSymbol == "IRON_ORE"
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "MINER-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns(new MiningAutomationPlanState
            {
                PlanId = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now,
                Opportunities =
                [
                    new MiningAutomationOpportunityState
                    {
                        OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                        TradeSymbol = "IRON_ORE",
                        SellWaypointSymbol = "X1-AB-MKT1",
                        Status = MarketAutomationOpportunityStatus.Pending,
                        AssignedShipSymbol = null,
                        FirstObservedAt = Now,
                        LastObservedAt = Now,
                        StopReason = "No idle mining drone available and purchase unavailable.",
                    }
                ]
            });

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.MiningAutomation,
            Arg.Is<MiningAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "MINER-1"
                && state.Opportunities[0].StopReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAgentCreditsChangedEvent_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns(new MiningAutomationPlanState
            {
                PlanId = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now,
                Opportunities =
                [
                    new MiningAutomationOpportunityState
                    {
                        OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                        TradeSymbol = "IRON_ORE",
                        SellWaypointSymbol = "X1-AB-MKT1",
                        Status = MarketAutomationOpportunityStatus.Pending,
                        AssignedShipSymbol = null,
                        FirstObservedAt = Now,
                        LastObservedAt = Now,
                        StopReason = "No idle mining drone available and purchase unavailable.",
                    }
                ]
            });

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID"),
            ]);

        await CreateService().Handle(new Domain.Events.AgentCreditsChangedEvent(10_000, 40_000));

        await _plans.Received(1).UpsertAsync(
            PlanTypes.MiningAutomation,
            Arg.Is<MiningAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "MINER-1"
                && state.Opportunities[0].StopReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleShipBecameIdleEvent_TransitionsPersistedPendingQueue_ToAssigned_WhenMinerBecomesAvailable()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, Arg.Any<CancellationToken>())
            .Returns(new MiningAutomationPlanState
            {
                PlanId = Guid.NewGuid(),
                CreatedAt = Now,
                UpdatedAt = Now,
                Opportunities =
                [
                    new MiningAutomationOpportunityState
                    {
                        OpportunityKey = "X1-AB-MKT1|IRON_ORE",
                        TradeSymbol = "IRON_ORE",
                        SellWaypointSymbol = "X1-AB-MKT1",
                        Status = MarketAutomationOpportunityStatus.Pending,
                        AssignedShipSymbol = null,
                        FirstObservedAt = Now,
                        LastObservedAt = Now,
                        StopReason = "No idle mining drone available and purchase unavailable.",
                    }
                ]
            });

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-AST", "ASTEROID"),
            ]);

        await CreateService().Handle(new Domain.Events.ShipBecameIdleEvent("MINER-1", "Finished previous assignment"));

        await _plans.Received(1).UpsertAsync(
            PlanTypes.MiningAutomation,
            Arg.Is<MiningAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "MINER-1"
                && state.Opportunities[0].StopReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_DoesNothing_WhenMarketIsNotScarceDemandMineral()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("FOOD", type: "IMPORT", supply: "MODERATE")),
                Snapshot("X1-AB-MKT2", Good("IRON_ORE", type: "EXCHANGE", supply: "MODERATE")),
                Snapshot("X1-AB-MKT3", Good("IRON_ORE", type: "EXPORT", supply: "SCARCE")),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_UsesClosestAsteroidToSellWaypoint_WhenMultipleMatchingSourcesExist()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns([
                Snapshot("X1-AB-MKT1", Good("IRON_ORE")),
            ]);

        _goals.GetActiveMineAndSellTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string SellWaypointSymbol, string TradeSymbol)>());

        var miner = Miner("MINER-1", waypoint: "X1-AB-HQ");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([miner]);
        _goals.GetActiveGoalAsync(miner.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        _waypoints.GetBySystemAsync("X1-AB", Arg.Any<CancellationToken>())
            .Returns([
                Waypoint("X1-AB-HQ", "ORBITAL_STATION", x: 100, y: 100),
                Waypoint("X1-AB-MKT1", "ORBITAL_STATION", x: 0, y: 0),
                Waypoint("X1-AB-AST-CLOSE", "ASTEROID", x: 3, y: 3, traitsJson: "COMMON_METAL_DEPOSITS"),
                Waypoint("X1-AB-AST-FAR", "ASTEROID", x: 40, y: 40, traitsJson: "COMMON_METAL_DEPOSITS"),
            ]);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).SetActiveGoalAsync(
            "MINER-1",
            Arg.Is<MineAndSellGoal>(goal => goal.SourceWaypointSymbol == "X1-AB-AST-CLOSE"),
            Arg.Any<CancellationToken>());
    }
}

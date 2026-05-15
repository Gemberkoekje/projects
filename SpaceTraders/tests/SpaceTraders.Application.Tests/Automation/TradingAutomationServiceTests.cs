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

public sealed class TradingAutomationServiceTests
{
    private readonly IMarketRepository _markets = Substitute.For<IMarketRepository>();
    private readonly IShipRepository _ships = Substitute.For<IShipRepository>();
    private readonly IShipGoalRepository _goals = Substitute.For<IShipGoalRepository>();
    private readonly IShipyardRepository _shipyards = Substitute.For<IShipyardRepository>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IPlanRepository _plans = Substitute.For<IPlanRepository>();
    private readonly ISpaceTradersPort _port = Substitute.For<ISpaceTradersPort>();
    private readonly IBudgetPolicy _budget = Substitute.For<IBudgetPolicy>();

    private TradingAutomationService CreateService() =>
        new(
            _markets,
            _ships,
            _goals,
            _shipyards,
            _agents,
            _plans,
            _port,
            _budget,
            NullLogger<TradingAutomationService>.Instance);

    private static MarketSnapshot Snapshot(string waypoint, params TradeGoodSnapshot[] goods) =>
        new(
            waypoint,
            "X1-AB",
            goods,
            imports: goods.Where(g => g.Type == "IMPORT").Select(g => g.Symbol).ToList(),
            exports: goods.Where(g => g.Type == "EXPORT").Select(g => g.Symbol).ToList(),
            exchange: goods.Where(g => g.Type == "EXCHANGE").Select(g => g.Symbol).ToList());

    private static TradeGoodSnapshot Good(string symbol, string type, string supply) =>
        new(symbol, type, 0, 0, 10, supply);

    private static ShipModel Trader(string symbol, string waypoint = "X1-AB-HQ", string status = "DOCKED") =>
        new(symbol, "X1-AB", waypoint, status, "CRUISE", 20, 40, CargoCapacity: 40, ShipType: "SHIP_LIGHT_HAULER");

    private static ShipyardWaypointDto TradeShipyard(string waypoint = "X1-AB-SY1", long price = 60_000) =>
        new()
        {
            WaypointSymbol = waypoint,
            SystemSymbol = "X1-AB",
            ShipTypes = ["SHIP_LIGHT_HAULER"],
            Ships =
            [
                new ShipyardShipDto
                {
                    Type = "SHIP_LIGHT_HAULER",
                    PurchasePrice = price,
                }
            ]
        };

    [Fact]
    public async Task EnsureBootstrappedAsync_AssignsIdleTradeShip_ForScarceNonMineralWithAbundantSource()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("FOOD", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("FOOD", "IMPORT", "SCARCE")),
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        var trader = Trader("TRADER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([trader]);
        _goals.GetActiveGoalAsync(trader.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).SetActiveGoalAsync(
            "TRADER-1",
            Arg.Is<TradeBetweenMarketsGoal>(g =>
                g.TradeSymbol == "FOOD"
                && g.BuyWaypointSymbol == "X1-AB-MKT-BUY"
                && g.SellWaypointSymbol == "X1-AB-MKT-SELL"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PurchasesTradeShip_WhenNoIdleTraderAndBudgetAllows()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("MEDICINE", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("MEDICINE", "IMPORT", "SCARCE")),
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TradeShipyard()]);
        _budget.EvaluateAsync(60_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(true, 200_000, 100_000, 100_000));

        _port.PurchaseShipAsync("SHIP_LIGHT_HAULER", "X1-AB-SY1", Arg.Any<CancellationToken>())
            .Returns(new PurchaseShipActionResult(
                new AgentModel("AGENT", null, "X1-AB-HQ", 140_000, "FACTION", 2),
                "TRADER-NEW",
                new NavModel("DOCKED", "X1-AB", "X1-AB-SY1", "CRUISE", null, null),
                new FuelModel(20, 40),
                60_000));

        await CreateService().EnsureBootstrappedAsync();

        await _port.Received(1).PurchaseShipAsync("SHIP_LIGHT_HAULER", "X1-AB-SY1", Arg.Any<CancellationToken>());
        await _goals.Received(1).SetActiveGoalAsync(
            "TRADER-NEW",
            Arg.Any<TradeBetweenMarketsGoal>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_KeepsGoalPending_WhenNoShipAndBudgetCannotAffordPurchase()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("CLOTHING", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("CLOTHING", "IMPORT", "SCARCE")),
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TradeShipyard()]);
        _budget.EvaluateAsync(60_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(false, 50_000, 100_000, -50_000, "reserve"));

        await CreateService().EnsureBootstrappedAsync();

        await _port.DidNotReceive().PurchaseShipAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _goals.DidNotReceive().SetActiveGoalAsync(Arg.Any<string>(), Arg.Any<ShipGoal>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PersistsPendingQueue_WhenNoTraderCanBeAssigned()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("FOOD", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("FOOD", "IMPORT", "SCARCE")),
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ShipModel>());
        _shipyards.GetAllAsync(Arg.Any<CancellationToken>()).Returns([TradeShipyard()]);
        _budget.EvaluateAsync(60_000, Arg.Any<CancellationToken>())
            .Returns(new BudgetDecision(false, 50_000, 100_000, -50_000, "reserve"));

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.TradingAutomation,
            Arg.Is<TradingAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].TradeSymbol == "FOOD"
                && state.Opportunities[0].BuyWaypointSymbol == "X1-AB-MKT-BUY"
                && state.Opportunities[0].SellWaypointSymbol == "X1-AB-MKT-SELL"
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Pending),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_PersistsAssignedQueue_WhenTraderIsAssigned()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("FOOD", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("FOOD", "IMPORT", "SCARCE")),
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        var trader = Trader("TRADER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([trader]);
        _goals.GetActiveGoalAsync(trader.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.TradingAutomation,
            Arg.Is<TradingAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].TradeSymbol == "FOOD"
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "TRADER-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_TransitionsPersistedPendingQueue_ToAssigned_WhenTraderBecomesAvailable()
    {
        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("FOOD", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("FOOD", "IMPORT", "SCARCE")),
            });

        _plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, Arg.Any<CancellationToken>())
            .Returns(new TradingAutomationPlanState
            {
                PlanId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Opportunities =
                [
                    new TradingAutomationOpportunityState
                    {
                        OpportunityKey = "X1-AB-MKT-BUY|X1-AB-MKT-SELL|FOOD",
                        TradeSymbol = "FOOD",
                        BuyWaypointSymbol = "X1-AB-MKT-BUY",
                        SellWaypointSymbol = "X1-AB-MKT-SELL",
                        Status = MarketAutomationOpportunityStatus.Pending,
                        AssignedShipSymbol = null,
                        FirstObservedAt = DateTimeOffset.UtcNow,
                        LastObservedAt = DateTimeOffset.UtcNow,
                        StopReason = "No idle trade ship available and purchase unavailable.",
                    }
                ]
            });

        _goals.GetActiveTradeRouteTargetsAsync(Arg.Any<CancellationToken>())
            .Returns(new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>());

        var trader = Trader("TRADER-1");
        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([trader]);
        _goals.GetActiveGoalAsync(trader.Symbol, Arg.Any<CancellationToken>())
            .Returns((ShipGoal?)null);

        await CreateService().EnsureBootstrappedAsync();

        await _plans.Received(1).UpsertAsync(
            PlanTypes.TradingAutomation,
            Arg.Is<TradingAutomationPlanState>(state =>
                state.Opportunities.Count == 1
                && state.Opportunities[0].Status == MarketAutomationOpportunityStatus.Assigned
                && state.Opportunities[0].AssignedShipSymbol == "TRADER-1"
                && state.Opportunities[0].StopReason == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnsureBootstrappedAsync_ClearsStaleTradingGoal_WhenSellMarketNoLongerScarce()
    {
        var trader = Trader("TRADER-STALE");

        _markets.GetAllSnapshotsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<MarketSnapshot>
            {
                Snapshot("X1-AB-MKT-BUY", Good("FOOD", "EXPORT", "ABUNDANT")),
                Snapshot("X1-AB-MKT-SELL", Good("FOOD", "IMPORT", "MODERATE")),
            });

        _ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns([trader]);
        _goals.GetActiveGoalAsync(trader.Symbol, Arg.Any<CancellationToken>())
            .Returns(new TradeBetweenMarketsGoal
            {
                TradeSymbol = "FOOD",
                BuyWaypointSymbol = "X1-AB-MKT-BUY",
                SellWaypointSymbol = "X1-AB-MKT-SELL",
            });

        await CreateService().EnsureBootstrappedAsync();

        await _goals.Received(1).ClearActiveGoalAsync("TRADER-STALE", Arg.Any<CancellationToken>());
    }
}

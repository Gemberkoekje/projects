using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

public interface ITradingAutomationService
{
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.MarketDataRefreshedEvent @event, CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.AgentCreditsChangedEvent @event, CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.ShipBecameIdleEvent @event, CancellationToken cancellationToken = default);
}

public sealed class TradingAutomationService(
    IMarketRepository markets,
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipyardRepository shipyards,
    IAgentRepository agents,
    IPlanRepository plans,
    ISpaceTradersPort port,
    IBudgetPolicy budget,
    ILogger<TradingAutomationService> logger) : ITradingAutomationService
{
    private const string ScarceSupply = "SCARCE";
    private const string AbundantSupply = "ABUNDANT";
    private const string ImportType = "IMPORT";
    private const string ExportType = "EXPORT";
    private const string ExchangeType = "EXCHANGE";
    private const string TradeShipType = "SHIP_LIGHT_HAULER";

    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var opportunities = await GetTradeOpportunitiesAsync(cancellationToken);
        var activeOpportunityKeys = BuildOpportunityKeySet(opportunities);

        await CleanupStaleTradingGoalsAsync(activeOpportunityKeys, cancellationToken);

        var queue = await ReconcileQueueAsync(opportunities, now, cancellationToken);
        if (opportunities.Count == 0)
        {
            await plans.UpsertAsync(PlanTypes.TradingAutomation, queue, cancellationToken);
            return;
        }

        var updatedQueue = await TryAssignOrPurchaseForOpportunitiesAsync(opportunities, queue, now, cancellationToken);
        await plans.UpsertAsync(PlanTypes.TradingAutomation, updatedQueue, cancellationToken);
    }

    public Task Handle(Domain.Events.MarketDataRefreshedEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    public Task Handle(Domain.Events.AgentCreditsChangedEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    public Task Handle(Domain.Events.ShipBecameIdleEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    private async Task CleanupStaleTradingGoalsAsync(
        IReadOnlySet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)> activeOpportunityKeys,
        CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in fleet)
        {
            var activeGoal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (activeGoal is not TradeBetweenMarketsGoal tradeGoal)
            {
                continue;
            }

            var key = CreateOpportunityKey(
                tradeGoal.BuyWaypointSymbol,
                tradeGoal.SellWaypointSymbol,
                tradeGoal.TradeSymbol);
            if (activeOpportunityKeys.Contains(key))
            {
                continue;
            }

            await goals.ClearActiveGoalAsync(ship.Symbol, cancellationToken);
            logger.LogInformation(
                "Trading automation: cleared stale trading goal for ship {ShipSymbol} ({TradeSymbol}: {BuyWaypoint} -> {SellWaypoint}) because market conditions changed.",
                ship.Symbol,
                tradeGoal.TradeSymbol,
                tradeGoal.BuyWaypointSymbol,
                tradeGoal.SellWaypointSymbol);
        }
    }

    private async Task<TradingAutomationPlanState> TryAssignOrPurchaseForOpportunitiesAsync(
        IReadOnlyList<TradingOpportunity> opportunities,
        TradingAutomationPlanState queue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeTargets = await goals.GetActiveTradeRouteTargetsAsync(cancellationToken);
        var fleet = await ships.GetAllAsync(cancellationToken);
        var shipsWithGoals = await LoadShipsWithActiveGoalAsync(fleet, cancellationToken);
        var queueItems = queue.Opportunities.ToDictionary(item => item.OpportunityKey, StringComparer.OrdinalIgnoreCase);

        foreach (var opportunity in opportunities)
        {
            var targetKey = CreateOpportunityKey(
                opportunity.BuyWaypointSymbol,
                opportunity.SellWaypointSymbol,
                opportunity.TradeSymbol);
            var queueKey = FormatOpportunityKey(targetKey.BuyWaypointSymbol, targetKey.SellWaypointSymbol, targetKey.TradeSymbol);
            if (activeTargets.Contains(targetKey))
            {
                if (queueItems.TryGetValue(queueKey, out var activeItem))
                {
                    queueItems[queueKey] = activeItem with
                    {
                        Status = MarketAutomationOpportunityStatus.Assigned,
                        LastObservedAt = now,
                        StopReason = null,
                    };
                }

                continue;
            }

            var idleTrader = FindIdleTradeShip(fleet, shipsWithGoals);
            if (idleTrader is null)
            {
                idleTrader = await TryPurchaseTradeShipAsync(opportunity.BuyWaypointSymbol, cancellationToken);
                if (idleTrader is null)
                {
                    logger.LogInformation(
                        "Trading automation: deferred goal for {TradeSymbol} from {BuyWaypoint} to {SellWaypoint}; no idle trade ship and purchase unavailable.",
                        opportunity.TradeSymbol,
                        opportunity.BuyWaypointSymbol,
                        opportunity.SellWaypointSymbol);

                    if (queueItems.TryGetValue(queueKey, out var pendingItem))
                    {
                        queueItems[queueKey] = pendingItem with
                        {
                            Status = MarketAutomationOpportunityStatus.Pending,
                            LastObservedAt = now,
                            StopReason = "No idle trade ship available and purchase unavailable.",
                        };
                    }

                    continue;
                }

                fleet = fleet.Append(idleTrader).ToList();
            }

            var goal = new TradeBetweenMarketsGoal
            {
                TradeSymbol = opportunity.TradeSymbol,
                BuyWaypointSymbol = opportunity.BuyWaypointSymbol,
                SellWaypointSymbol = opportunity.SellWaypointSymbol,
            };

            await goals.SetActiveGoalAsync(idleTrader.Symbol, goal, cancellationToken);
            shipsWithGoals.Add(idleTrader.Symbol);

            var updatedTargets = activeTargets.ToHashSet();
            updatedTargets.Add(targetKey);
            activeTargets = updatedTargets;

            if (queueItems.TryGetValue(queueKey, out var assignedItem))
            {
                queueItems[queueKey] = assignedItem with
                {
                    Status = MarketAutomationOpportunityStatus.Assigned,
                    AssignedShipSymbol = idleTrader.Symbol,
                    LastObservedAt = now,
                    StopReason = null,
                };
            }

            logger.LogInformation(
                "Trading automation: assigned ship {ShipSymbol} to buy {TradeSymbol} at {BuyWaypoint} and sell at {SellWaypoint}.",
                idleTrader.Symbol,
                opportunity.TradeSymbol,
                opportunity.BuyWaypointSymbol,
                opportunity.SellWaypointSymbol);
        }

        return queue with
        {
            Opportunities = queueItems.Values
                .OrderBy(item => item.BuyWaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TradeSymbol, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UpdatedAt = now,
        };
    }

    private static ShipModel? FindIdleTradeShip(
        IReadOnlyList<ShipModel> shipsInFleet,
        IReadOnlySet<string> shipsWithGoals)
    {
        return shipsInFleet
            .Where(ship =>
                ship.IsTradingCapable
                && ship.LocalStatus != Domain.Enums.ShipLocalStatus.InTransit
                && ship.FuelCurrent > 0
                && ship.CargoCapacity > ship.CargoCurrent
                && !shipsWithGoals.Contains(ship.Symbol))
            .OrderBy(ship => ship.IsMiningCapable ? 1 : 0)
            .FirstOrDefault();
    }

    private async Task<HashSet<string>> LoadShipsWithActiveGoalAsync(
        IReadOnlyList<ShipModel> fleet,
        CancellationToken cancellationToken)
    {
        var withGoals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet)
        {
            var activeGoal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (activeGoal is not null
                && activeGoal.Status is not Domain.Enums.GoalStatus.Completed
                and not Domain.Enums.GoalStatus.Blocked)
            {
                withGoals.Add(ship.Symbol);
            }
        }

        return withGoals;
    }

    private async Task<ShipModel?> TryPurchaseTradeShipAsync(
        string buyWaypointSymbol,
        CancellationToken cancellationToken)
    {
        var shipyardOptions = await shipyards.GetAllAsync(cancellationToken);
        if (shipyardOptions.Count == 0)
        {
            return null;
        }

        var preferredSystem = ExtractSystemSymbol(buyWaypointSymbol);
        var shipyard = shipyardOptions.FirstOrDefault(candidate =>
            candidate.SystemSymbol.Equals(preferredSystem, StringComparison.OrdinalIgnoreCase)
            && candidate.ShipTypes.Contains(TradeShipType, StringComparer.OrdinalIgnoreCase))
            ?? shipyardOptions.FirstOrDefault(candidate =>
                candidate.ShipTypes.Contains(TradeShipType, StringComparer.OrdinalIgnoreCase));

        if (shipyard is null)
        {
            return null;
        }

        var estimatedCost = shipyard.Ships
            .FirstOrDefault(s => s.Type.Equals(TradeShipType, StringComparison.OrdinalIgnoreCase))
            ?.PurchasePrice ?? 0;

        if (estimatedCost <= 0)
        {
            logger.LogInformation(
                "Trading automation: trade ship purchase skipped at {Shipyard} — purchase price unknown (shipyard not yet visited with a ship).",
                shipyard.WaypointSymbol);
            return null;
        }

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            logger.LogInformation(
                "Trading automation: trade ship purchase denied at {Shipyard} — {Reason}.",
                shipyard.WaypointSymbol,
                decision.Reason);
            return null;
        }

        var purchased = await port.PurchaseShipAsync(TradeShipType, shipyard.WaypointSymbol, cancellationToken);
        await agents.UpsertAsync(purchased.Agent, cancellationToken);

        var ship = new ShipModel(
            purchased.ShipSymbol,
            purchased.ShipNav.SystemSymbol,
            purchased.ShipNav.WaypointSymbol,
            purchased.ShipNav.Status,
            purchased.ShipNav.FlightMode,
            purchased.ShipFuel.Current,
            purchased.ShipFuel.Capacity,
            purchased.ShipNav.ArrivesAt,
            purchased.ShipNav.DestWaypointSymbol,
            purchased.ShipCargo.Units,
            purchased.ShipCargo.Capacity,
            ShipType: TradeShipType,
            CargoInventory: purchased.ShipCargo.Inventory);

        await ships.UpsertAsync(ship, cancellationToken);

        logger.LogInformation(
            "Trading automation: purchased trade ship {ShipSymbol} at {Shipyard}.",
            purchased.ShipSymbol,
            shipyard.WaypointSymbol);

        return ship;
    }

    private async Task<IReadOnlyList<TradingOpportunity>> GetTradeOpportunitiesAsync(CancellationToken cancellationToken)
    {
        var snapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var opportunities = new List<TradingOpportunity>();

        foreach (var sellSnapshot in snapshots)
        {
            foreach (var good in sellSnapshot.TradeGoods)
            {
                if (!good.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase)
                    || !good.Supply.Equals(ScarceSupply, StringComparison.OrdinalIgnoreCase)
                    || IsMineralSymbol(good.Symbol))
                {
                    continue;
                }

                var buySnapshot = snapshots.FirstOrDefault(candidate =>
                    !candidate.WaypointSymbol.Equals(sellSnapshot.WaypointSymbol, StringComparison.OrdinalIgnoreCase)
                    && candidate.TradeGoods.Any(candidateGood =>
                        candidateGood.Symbol.Equals(good.Symbol, StringComparison.OrdinalIgnoreCase)
                        && candidateGood.Supply.Equals(AbundantSupply, StringComparison.OrdinalIgnoreCase)
                        && IsSellableAtSource(candidateGood.Type)));

                if (buySnapshot is null)
                {
                    continue;
                }

                opportunities.Add(new TradingOpportunity(
                    buySnapshot.WaypointSymbol,
                    sellSnapshot.WaypointSymbol,
                    good.Symbol));
            }
        }

        return opportunities;
    }

    private static bool IsSellableAtSource(string tradeType) =>
        tradeType.Equals(ExportType, StringComparison.OrdinalIgnoreCase)
        || tradeType.Equals(ExchangeType, StringComparison.OrdinalIgnoreCase);

    private static HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)> BuildOpportunityKeySet(
        IReadOnlyList<TradingOpportunity> opportunities)
    {
        var keys = new HashSet<(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol)>();
        foreach (var opportunity in opportunities)
        {
            keys.Add(CreateOpportunityKey(
                opportunity.BuyWaypointSymbol,
                opportunity.SellWaypointSymbol,
                opportunity.TradeSymbol));
        }

        return keys;
    }

    private static (string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol) CreateOpportunityKey(
        string buyWaypointSymbol,
        string sellWaypointSymbol,
        string tradeSymbol) =>
        (
            buyWaypointSymbol.ToUpperInvariant(),
            sellWaypointSymbol.ToUpperInvariant(),
            tradeSymbol.ToUpperInvariant()
        );

    private static bool IsMineralSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        var upper = symbol.ToUpperInvariant();
        return upper.Contains("_ORE", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("CRYSTAL", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("QUARTZ", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("DIAMOND", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("_ICE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<TradingAutomationPlanState> ReconcileQueueAsync(
        IReadOnlyList<TradingOpportunity> opportunities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await plans.GetAsync<TradingAutomationPlanState>(PlanTypes.TradingAutomation, cancellationToken);
        var existingItems = existing?.Opportunities.ToDictionary(item => item.OpportunityKey, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, TradingAutomationOpportunityState>(StringComparer.OrdinalIgnoreCase);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var opportunity in opportunities)
        {
            var key = FormatOpportunityKey(
                opportunity.BuyWaypointSymbol.ToUpperInvariant(),
                opportunity.SellWaypointSymbol.ToUpperInvariant(),
                opportunity.TradeSymbol.ToUpperInvariant());
            activeKeys.Add(key);

            if (existingItems.TryGetValue(key, out var current))
            {
                existingItems[key] = current with
                {
                    LastObservedAt = now,
                    StopReason = current.Status == MarketAutomationOpportunityStatus.Pending ? current.StopReason : null,
                };
            }
            else
            {
                existingItems[key] = new TradingAutomationOpportunityState
                {
                    OpportunityKey = key,
                    TradeSymbol = opportunity.TradeSymbol,
                    BuyWaypointSymbol = opportunity.BuyWaypointSymbol,
                    SellWaypointSymbol = opportunity.SellWaypointSymbol,
                    Status = MarketAutomationOpportunityStatus.Pending,
                    AssignedShipSymbol = null,
                    FirstObservedAt = now,
                    LastObservedAt = now,
                    StopReason = null,
                };
            }
        }

        var reconciled = existingItems.Values
            .Where(item => activeKeys.Contains(item.OpportunityKey))
            .OrderBy(item => item.BuyWaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TradeSymbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TradingAutomationPlanState
        {
            PlanId = existing?.PlanId ?? Guid.NewGuid(),
            Opportunities = reconciled,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var parts = waypointSymbol.Split('-');
        return parts.Length >= 2 ? $"{parts[0]}-{parts[1]}" : waypointSymbol;
    }

    private static string FormatOpportunityKey(string buyWaypointSymbol, string sellWaypointSymbol, string tradeSymbol) =>
        $"{buyWaypointSymbol}|{sellWaypointSymbol}|{tradeSymbol}";

    private sealed record TradingOpportunity(string BuyWaypointSymbol, string SellWaypointSymbol, string TradeSymbol);
}

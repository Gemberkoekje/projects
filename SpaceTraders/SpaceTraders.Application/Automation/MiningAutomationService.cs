using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

public interface IMiningAutomationService
{
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.MarketDataRefreshedEvent @event, CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.AgentCreditsChangedEvent @event, CancellationToken cancellationToken = default);

    Task Handle(Domain.Events.ShipBecameIdleEvent @event, CancellationToken cancellationToken = default);
}

public sealed class MiningAutomationService(
    IMarketRepository markets,
    IShipRepository ships,
    IShipGoalRepository goals,
    IWaypointRepository waypoints,
    IShipyardRepository shipyards,
    IAgentRepository agents,
    ISettingsRepository settings,
    IPlanRepository plans,
    ISpaceTradersPort port,
    IBudgetPolicy budget,
    ILogger<MiningAutomationService> logger) : IMiningAutomationService
{
    private const string ScarceSupply = "SCARCE";
    private const string ImportType = "IMPORT";
    private const string MiningDroneShipType = "SHIP_MINING_DRONE";
    private const string MaxMiningDronesSettingKey = "Mining.MaxDrones";
    private const int DefaultMaxMiningDrones = 20;

    private static readonly IReadOnlySet<string> KnownMineralSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ALUMINUM_ORE",
        "AMMONIA_ICE",
        "COPPER_ORE",
        "DIAMONDS",
        "GOLD_ORE",
        "ICE_WATER",
        "IRON_ORE",
        "MERITIUM_ORE",
        "PLATINUM_ORE",
        "QUARTZ_SAND",
        "SILICON_CRYSTALS",
        "SILVER_ORE",
    };

    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var now = TimeProvider.System.GetUtcNow();
        var opportunities = await GetScarceMineralBuyOpportunitiesAsync(cancellationToken);
        var activeOpportunityKeys = BuildOpportunityKeySet(opportunities);

        await CleanupStaleMiningGoalsAsync(activeOpportunityKeys, cancellationToken);

        var queue = await ReconcileQueueAsync(opportunities, now, cancellationToken);
        if (opportunities.Count == 0)
        {
            await plans.UpsertAsync(PlanTypes.MiningAutomation, queue, cancellationToken);
            return;
        }

        var updatedQueue = await TryAssignOrPurchaseForOpportunitiesAsync(opportunities, queue, now, cancellationToken);
        await plans.UpsertAsync(PlanTypes.MiningAutomation, updatedQueue, cancellationToken);
    }

    public Task Handle(Domain.Events.MarketDataRefreshedEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    public Task Handle(Domain.Events.AgentCreditsChangedEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    public Task Handle(Domain.Events.ShipBecameIdleEvent @event, CancellationToken cancellationToken = default) =>
        EnsureBootstrappedAsync(cancellationToken);

    private async Task CleanupStaleMiningGoalsAsync(
        IReadOnlySet<(string SellWaypointSymbol, string TradeSymbol)> activeOpportunityKeys,
        CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);

        foreach (var ship in fleet)
        {
            var activeGoal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (activeGoal is not MineAndSellGoal miningGoal)
            {
                continue;
            }

            var key = CreateOpportunityKey(miningGoal.SellWaypointSymbol, miningGoal.TradeSymbol);
            if (activeOpportunityKeys.Contains(key))
            {
                continue;
            }

            await goals.ClearActiveGoalAsync(ship.Symbol, cancellationToken);
            logger.LogInformation(
                "Mining automation: cleared stale mining goal for ship {ShipSymbol} ({TradeSymbol} -> {SellWaypoint}) because market is no longer SCARCE.",
                ship.Symbol,
                miningGoal.TradeSymbol,
                miningGoal.SellWaypointSymbol);
        }
    }

    private async Task<MiningAutomationPlanState> TryAssignOrPurchaseForOpportunitiesAsync(
        IReadOnlyList<MiningOpportunity> opportunities,
        MiningAutomationPlanState queue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeTargets = await goals.GetActiveMineAndSellTargetsAsync(cancellationToken);
        var assignedShips = await ships.GetAllAsync(cancellationToken);
        var shipsWithGoals = await LoadShipsWithActiveGoalAsync(assignedShips, cancellationToken);
        var queueItems = queue.Opportunities.ToDictionary(item => item.OpportunityKey, StringComparer.OrdinalIgnoreCase);

        foreach (var opportunity in opportunities)
        {
            var targetKey = CreateOpportunityKey(
                opportunity.SellWaypointSymbol,
                opportunity.TradeSymbol);
            var queueKey = FormatOpportunityKey(targetKey.SellWaypointSymbol, targetKey.TradeSymbol);

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

            var idleDrone = FindIdleMiningDrone(assignedShips, shipsWithGoals);
            if (idleDrone is null)
            {
                idleDrone = await TryPurchaseMiningDroneAsync(opportunity.SellWaypointSymbol, assignedShips, cancellationToken);
                if (idleDrone is null)
                {
                    logger.LogInformation(
                        "Mining automation: deferred goal for {TradeSymbol} at {SellWaypoint}; no idle drone and purchase unavailable.",
                        opportunity.TradeSymbol,
                        opportunity.SellWaypointSymbol);

                    if (queueItems.TryGetValue(queueKey, out var pendingItem))
                    {
                        queueItems[queueKey] = pendingItem with
                        {
                            Status = MarketAutomationOpportunityStatus.Pending,
                            LastObservedAt = now,
                            StopReason = "No idle mining drone available and purchase unavailable.",
                        };
                    }

                    continue;
                }

                assignedShips = assignedShips.Append(idleDrone).ToList();
            }

            var sourceWaypoint = await ResolveSourceAsteroidAsync(idleDrone, opportunity.TradeSymbol, cancellationToken);
            if (string.IsNullOrWhiteSpace(sourceWaypoint))
            {
                logger.LogInformation(
                    "Mining automation: deferred goal for {TradeSymbol} at {SellWaypoint}; no asteroid source found.",
                    opportunity.TradeSymbol,
                    opportunity.SellWaypointSymbol);

                if (queueItems.TryGetValue(queueKey, out var deferredItem))
                {
                    queueItems[queueKey] = deferredItem with
                    {
                        Status = MarketAutomationOpportunityStatus.Pending,
                        LastObservedAt = now,
                        StopReason = "No asteroid source waypoint found.",
                    };
                }

                continue;
            }

            var goal = new MineAndSellGoal
            {
                TradeSymbol = opportunity.TradeSymbol,
                SourceWaypointSymbol = sourceWaypoint,
                SellWaypointSymbol = opportunity.SellWaypointSymbol,
            };

            await goals.SetActiveGoalAsync(idleDrone.Symbol, goal, cancellationToken);
            shipsWithGoals.Add(idleDrone.Symbol);

            var updatedTargets = activeTargets.ToHashSet();
            updatedTargets.Add(targetKey);
            activeTargets = updatedTargets;

            if (queueItems.TryGetValue(queueKey, out var assignedItem))
            {
                queueItems[queueKey] = assignedItem with
                {
                    Status = MarketAutomationOpportunityStatus.Assigned,
                    AssignedShipSymbol = idleDrone.Symbol,
                    LastObservedAt = now,
                    StopReason = null,
                };
            }

            logger.LogInformation(
                "Mining automation: assigned ship {ShipSymbol} to mine {TradeSymbol} from {SourceWaypoint} and sell at {SellWaypoint}.",
                idleDrone.Symbol,
                opportunity.TradeSymbol,
                sourceWaypoint,
                opportunity.SellWaypointSymbol);
        }

        return queue with
        {
            Opportunities = queueItems.Values
                .OrderBy(item => item.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TradeSymbol, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            UpdatedAt = now,
        };
    }

    private static ShipModel? FindIdleMiningDrone(
        IReadOnlyList<ShipModel> shipsInFleet,
        IReadOnlySet<string> shipsWithGoals)
    {
        return shipsInFleet.FirstOrDefault(ship =>
            ship.ShipType.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase)
            && ship.LocalStatus != Domain.Enums.ShipLocalStatus.InTransit
            && !shipsWithGoals.Contains(ship.Symbol));
    }

    private async Task<HashSet<string>> LoadShipsWithActiveGoalAsync(
        IReadOnlyList<ShipModel> fleet,
        CancellationToken cancellationToken)
    {
        var withGoals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in fleet)
        {
            var activeGoal = await goals.GetActiveGoalAsync(ship.Symbol, cancellationToken);
            if (activeGoal is not null)
            {
                withGoals.Add(ship.Symbol);
            }
        }

        return withGoals;
    }

    private async Task<ShipModel?> TryPurchaseMiningDroneAsync(
        string sellWaypointSymbol,
        IReadOnlyList<ShipModel> currentFleet,
        CancellationToken cancellationToken)
    {
        var maxMiningDrones = await GetMaxMiningDronesAsync(cancellationToken);
        var miningDroneCount = currentFleet.Count(ship =>
            ship.ShipType.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase));

        if (miningDroneCount >= maxMiningDrones)
        {
            logger.LogInformation(
                "Mining automation: mining drone cap reached ({Current}/{Max}); purchase skipped.",
                miningDroneCount,
                maxMiningDrones);
            return null;
        }

        var shipyardOptions = await shipyards.GetAllAsync(cancellationToken);
        if (shipyardOptions.Count == 0)
        {
            return null;
        }

        var preferredSystem = ExtractSystemSymbol(sellWaypointSymbol);
        var shipyard = shipyardOptions.FirstOrDefault(candidate =>
            candidate.SystemSymbol.Equals(preferredSystem, StringComparison.OrdinalIgnoreCase)
            && candidate.ShipTypes.Contains(MiningDroneShipType, StringComparer.OrdinalIgnoreCase))
            ?? shipyardOptions.FirstOrDefault(candidate =>
                candidate.ShipTypes.Contains(MiningDroneShipType, StringComparer.OrdinalIgnoreCase));

        if (shipyard is null)
        {
            return null;
        }

        var estimatedCost = shipyard.Ships
            .FirstOrDefault(s => s.Type.Equals(MiningDroneShipType, StringComparison.OrdinalIgnoreCase))
            ?.PurchasePrice ?? 0;

        var decision = await budget.EvaluateAsync(estimatedCost, cancellationToken);
        if (!decision.CanAfford)
        {
            logger.LogInformation(
                "Mining automation: mining drone purchase denied at {Shipyard} — {Reason}.",
                shipyard.WaypointSymbol,
                decision.Reason);
            return null;
        }

        var purchased = await port.PurchaseShipAsync(MiningDroneShipType, shipyard.WaypointSymbol, cancellationToken);
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
            ShipType: MiningDroneShipType,
            MountSymbols: ["MOUNT_MINING_LASER_I"]);

        await ships.UpsertAsync(ship, cancellationToken);

        logger.LogInformation(
            "Mining automation: purchased mining drone {ShipSymbol} at {Shipyard}.",
            purchased.ShipSymbol,
            shipyard.WaypointSymbol);

        return ship;
    }

    private async Task<IReadOnlyList<MiningOpportunity>> GetScarceMineralBuyOpportunitiesAsync(CancellationToken cancellationToken)
    {
        var snapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var opportunities = new List<MiningOpportunity>();

        foreach (var snapshot in snapshots)
        {
            foreach (var good in snapshot.TradeGoods)
            {
                if (!good.Type.Equals(ImportType, StringComparison.OrdinalIgnoreCase)
                    || !good.Supply.Equals(ScarceSupply, StringComparison.OrdinalIgnoreCase)
                    || !IsMineralSymbol(good.Symbol))
                {
                    continue;
                }

                opportunities.Add(new MiningOpportunity(snapshot.WaypointSymbol, good.Symbol));
            }
        }

        return opportunities;
    }

    private static HashSet<(string SellWaypointSymbol, string TradeSymbol)> BuildOpportunityKeySet(
        IReadOnlyList<MiningOpportunity> opportunities)
    {
        var keys = new HashSet<(string SellWaypointSymbol, string TradeSymbol)>();
        foreach (var opportunity in opportunities)
        {
            keys.Add(CreateOpportunityKey(opportunity.SellWaypointSymbol, opportunity.TradeSymbol));
        }

        return keys;
    }

    private static (string SellWaypointSymbol, string TradeSymbol) CreateOpportunityKey(
        string sellWaypointSymbol,
        string tradeSymbol) =>
        (
            sellWaypointSymbol.ToUpperInvariant(),
            tradeSymbol.ToUpperInvariant()
        );

    private async Task<int> GetMaxMiningDronesAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.GetAsync<int>(MaxMiningDronesSettingKey, cancellationToken);
        if (configured > 0)
        {
            return configured;
        }

        return DefaultMaxMiningDrones;
    }

    private async Task<string> ResolveSourceAsteroidAsync(ShipModel ship, string tradeSymbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.SystemSymbol))
        {
            return string.Empty;
        }

        var systemWaypoints = await waypoints.GetBySystemAsync(ship.SystemSymbol, cancellationToken);
        var currentWaypoint = string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            ? null
            : systemWaypoints.FirstOrDefault(w => w.Symbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase));

        var asteroid = systemWaypoints
            .Where(w => w.Type.Contains("ASTEROID", StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => DistanceFrom(currentWaypoint, w))
            .ThenByDescending(w => ScoreTradeSymbolMatch(w, tradeSymbol))
            .ThenBy(w => w.LastObservedAt)
            .FirstOrDefault();

        return asteroid?.Symbol ?? string.Empty;
    }

    private static decimal DistanceFrom(WaypointCacheModel? from, WaypointCacheModel to)
    {
        if (from is null)
        {
            return decimal.MaxValue;
        }

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return (decimal)Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static int ScoreTradeSymbolMatch(WaypointCacheModel waypoint, string tradeSymbol)
    {
        var haystack = $"{waypoint.TraitsJson} {waypoint.ModifiersJson}";
        var score = 0;

        if (haystack.Contains(tradeSymbol, StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (tradeSymbol.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase)
            && haystack.Contains("ORE", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static bool IsMineralSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        if (KnownMineralSymbols.Contains(symbol))
        {
            return true;
        }

        var upper = symbol.ToUpperInvariant();
        return upper.Contains("_ORE", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("CRYSTAL", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("QUARTZ", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("DIAMOND", StringComparison.OrdinalIgnoreCase)
            || upper.Contains("_ICE", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MiningAutomationPlanState> ReconcileQueueAsync(
        IReadOnlyList<MiningOpportunity> opportunities,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await plans.GetAsync<MiningAutomationPlanState>(PlanTypes.MiningAutomation, cancellationToken);
        var existingItems = existing?.Opportunities.ToDictionary(item => item.OpportunityKey, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, MiningAutomationOpportunityState>(StringComparer.OrdinalIgnoreCase);
        var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var opportunity in opportunities)
        {
            var key = FormatOpportunityKey(
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
                existingItems[key] = new MiningAutomationOpportunityState
                {
                    OpportunityKey = key,
                    TradeSymbol = opportunity.TradeSymbol,
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
            .OrderBy(item => item.SellWaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TradeSymbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MiningAutomationPlanState
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

    private static string FormatOpportunityKey(string sellWaypointSymbol, string tradeSymbol) =>
        $"{sellWaypointSymbol}|{tradeSymbol}";

    private sealed record MiningOpportunity(string SellWaypointSymbol, string TradeSymbol);
}

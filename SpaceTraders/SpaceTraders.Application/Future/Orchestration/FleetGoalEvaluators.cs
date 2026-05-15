using System.Text.Json;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Orchestration;

/// <summary>
/// Produces strategic goals from world state. Each evaluator focuses on a single
/// concern: contracts, construction, market coverage, or fleet expansion.
/// </summary>
public interface IFleetGoalEvaluator
{
    Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed class ContractGoalEvaluator(IContractRepository contracts) : IFleetGoalEvaluator
{
    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var active = await contracts.GetActiveAsync(cancellationToken);
        var goals = new List<FleetGoal>();
        foreach (var contract in active)
        {
            if (!contract.IsAccepted || contract.IsFulfilled)
            {
                continue;
            }

            var deliverables = DeserializeDeliverables(contract.DeliverablesJson);
            foreach (var deliverable in deliverables)
            {
                var remaining = deliverable.UnitsRequired - deliverable.UnitsFulfilled;
                if (remaining <= 0)
                {
                    continue;
                }

                goals.Add(new FleetGoal(
                    Kind: FleetGoalKind.Contract,
                    Description: $"Deliver {remaining} {deliverable.TradeSymbol} to {deliverable.DestinationSymbol} for contract {contract.Id}.",
                    Priority: FleetGoalPriority.Contract,
                    ContractId: contract.Id,
                    TradeSymbol: deliverable.TradeSymbol,
                    RemainingUnits: remaining,
                    Deadline: contract.TermsDeadline ?? contract.Expiration));
            }
        }

        return goals;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

public sealed class ConstructionGoalEvaluator(
    IConstructionRepository constructions,
    IMarketRepository markets,
    ITradeAnalyser tradeAnalyser) : IFleetGoalEvaluator
{
    private static readonly string[] ProtectedConstructionSymbols = ["ADVANCED_CIRCUITRY", "ADVANCED_CIRCUIT"];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SupplyChainDependencies =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FAB_MATS"] = ["IRON", "ALUMINUM"],
            ["FAB_MAT"] = ["IRON", "ALUMINUM"],
            ["ADVANCED_CIRCUITRY"] = ["ELECTRONICS", "MICROPROCESSORS"],
            ["ADVANCED_CIRCUIT"] = ["ELECTRONICS", "MICROPROCESSORS"],
            ["ELECTRONICS"] = ["SILICON_CRYSTALS", "COPPER"],
            ["MICROPROCESSORS"] = ["SILICON_CRYSTALS", "COPPER"],
            ["COPPER"] = ["COPPER_ORE"],
            ["IRON"] = ["IRON_ORE"],
        };

    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var sites = await constructions.GetIncompleteAsync(cancellationToken);
        if (sites.Count == 0)
        {
            return [];
        }

        var marketSnapshots = await markets.GetAllSnapshotsAsync(cancellationToken);
        var productionChain = tradeAnalyser.BuildProductionChain(marketSnapshots);
        var goals = new List<FleetGoal>();
        var emittedPrecursors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites)
        {
            foreach (var material in site.Materials)
            {
                var remaining = material.Required - material.Fulfilled;
                if (remaining <= 0)
                {
                    continue;
                }

                goals.Add(new FleetGoal(
                    Kind: FleetGoalKind.Construction,
                    Description: $"Supply {remaining} {material.TradeSymbol} to construction site {site.WaypointSymbol}.",
                    Priority: FleetGoalPriority.Construction,
                    TradeSymbol: material.TradeSymbol,
                    DestinationWaypoint: site.WaypointSymbol,
                    RemainingUnits: remaining));

                foreach (var precursor in BuildPrecursorGoals(material.TradeSymbol, site.WaypointSymbol, marketSnapshots, productionChain, emittedPrecursors))
                {
                    goals.Add(precursor);
                }
            }
        }

        return goals;
    }

    private static IEnumerable<FleetGoal> BuildPrecursorGoals(
        string tradeSymbol,
        string siteWaypoint,
        IReadOnlyList<MarketSnapshot> marketSnapshots,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> productionChain,
        ISet<string> emittedPrecursors)
    {
        var queue = new Queue<(string Symbol, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((tradeSymbol, 0));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current.Symbol))
            {
                continue;
            }

            if (!SupplyChainDependencies.TryGetValue(current.Symbol, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                queue.Enqueue((dependency, current.Depth + 1));

                var market = FindMostConstrainedProducerMarket(dependency, marketSnapshots, productionChain);
                if (market is null)
                {
                    continue;
                }

                var good = market.TradeGoods.FirstOrDefault(g => g.Symbol.Equals(dependency, StringComparison.OrdinalIgnoreCase));
                if (good is null || !IsConstrainedSupply(good.Supply))
                {
                    continue;
                }

                var naturalKey = $"{siteWaypoint}|{dependency}|{market.WaypointSymbol}";
                if (!emittedPrecursors.Add(naturalKey))
                {
                    continue;
                }

                var priority = FleetGoalPriority.ConstructionPrecursor + Math.Max(0, current.Depth);
                yield return new FleetGoal(
                    Kind: FleetGoalKind.Construction,
                    Description: $"Prime supply chain: supply {dependency} to {market.WaypointSymbol} for construction site {siteWaypoint}.",
                    Priority: priority,
                    TradeSymbol: dependency,
                    DestinationWaypoint: siteWaypoint,
                    OriginWaypoint: market.WaypointSymbol,
                    IsSupplyChainPrecursor: true);
            }
        }
    }

    private static MarketSnapshot? FindMostConstrainedProducerMarket(
        string tradeSymbol,
        IReadOnlyList<MarketSnapshot> marketSnapshots,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> productionChain)
    {
        return marketSnapshots
            .Where(m => m.TradeGoods.Any(g => g.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase)))
            .Where(m =>
                m.Exports.Any(e => e.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
                || m.Exchange.Any(e => e.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase))
                || productionChain.TryGetValue(tradeSymbol, out var consumers) && consumers.Count > 0)
            .OrderBy(m => ResolveSupplyRank(m.TradeGoods.First(g => g.Symbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase)).Supply))
            .ThenBy(m => m.WaypointSymbol, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool IsConstrainedSupply(string supply)
        => supply.Equals("SCARCE", StringComparison.OrdinalIgnoreCase)
            || supply.Equals("LIMITED", StringComparison.OrdinalIgnoreCase);

    private static int ResolveSupplyRank(string supply)
    {
        if (supply.Equals("SCARCE", StringComparison.OrdinalIgnoreCase)) return 0;
        if (supply.Equals("LIMITED", StringComparison.OrdinalIgnoreCase)) return 1;
        if (supply.Equals("MODERATE", StringComparison.OrdinalIgnoreCase)) return 2;
        if (supply.Equals("HIGH", StringComparison.OrdinalIgnoreCase)) return 3;
        if (supply.Equals("ABUNDANT", StringComparison.OrdinalIgnoreCase)) return 4;
        return 5;
    }
}

public sealed class MarketCoverageGoalEvaluator(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IShipAssignmentRepository assignments) : IFleetGoalEvaluator
{
    private const string MarketProbeAssignmentType = "MarketProbe";

    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        if (fleet.Count == 0)
        {
            return [];
        }

        var systems = fleet
            .Select(s => s.SystemSymbol)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (systems.Count == 0)
        {
            return [];
        }

        var knownMarkets = new List<WaypointCacheModel>();
        foreach (var system in systems)
        {
            var systemWaypoints = await waypoints.GetBySystemAsync(system!, cancellationToken);
            foreach (var waypoint in systemWaypoints.Where(w => w.HasMarket))
            {
                knownMarkets.Add(waypoint);
            }
        }

        if (knownMarkets.Count == 0)
        {
            return [];
        }

        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var coveredMarkets = activeAssignments
            .Where(a =>
                a.AssignmentType.Equals(MarketProbeAssignmentType, StringComparison.OrdinalIgnoreCase)
                && !a.CompletedAt.HasValue
                && !string.IsNullOrWhiteSpace(a.OriginWaypoint))
            .Select(a => a.OriginWaypoint!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var goals = new List<FleetGoal>();
        foreach (var market in knownMarkets
            .Where(m => !coveredMarkets.Contains(m.Symbol))
            .OrderBy(ResolveProbeDeploymentRank)
            .ThenBy(m => m.LastObservedAt)
            .ThenBy(m => m.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            goals.Add(new FleetGoal(
                Kind: FleetGoalKind.MarketCoverage,
                Description: $"Place market probe at {market.Symbol}.",
                Priority: FleetGoalPriority.MarketCoverage,
                OriginWaypoint: market.Symbol));
        }

        return goals;
    }

    private static int ResolveProbeDeploymentRank(WaypointCacheModel waypoint)
    {
        var traits = waypoint.TraitsJson ?? string.Empty;
        var type = waypoint.Type;

        if (IsPlanetOrMoon(type) && HasAnyTrait(traits, "INDUSTRIAL", "MANUFACTURING", "FACTORY", "REFINERY"))
        {
            return 0;
        }

        if (HasAnyTrait(traits, "HIGH_TECH", "RESEARCH_FACILITY"))
        {
            return 1;
        }

        if (waypoint.HasShipyard)
        {
            return 2;
        }

        if (type.Contains("ASTEROID_BASE", StringComparison.OrdinalIgnoreCase) || HasAnyTrait(traits, "PIRATE_BASE"))
        {
            return 3;
        }

        if (HasAnyTrait(traits, "FUEL_STATION"))
        {
            return 4;
        }

        return 5;
    }

    private static bool IsPlanetOrMoon(string type)
        => type.Contains("PLANET", StringComparison.OrdinalIgnoreCase)
            || type.Contains("MOON", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyTrait(string traitsJson, params string[] symbols)
        => symbols.Any(symbol => traitsJson.Contains(symbol, StringComparison.OrdinalIgnoreCase));
}

public sealed class FleetExpansionGoalEvaluator(
    IFleetCapacityEstimator capacity,
    IBudgetPolicy budget,
    ISettingsRepository settings,
    IAgentRepository agents,
    IShipyardRepository shipyards) : IFleetGoalEvaluator
{
    // Ship types ordered by preference for each role
    private static readonly string[] MiningShipTypes = ["SHIP_MINING_DRONE", "SHIP_ORE_HOUND"];
    private static readonly string[] HaulingShipTypes = ["SHIP_LIGHT_HAULER", "SHIP_HEAVY_FREIGHTER"];
    private static readonly string[] ScoutingShipTypes = ["SHIP_PROBE", "SHIP_SATELLITE"];

    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            return [];
        }

        var maxShips = await settings.GetAsync<int>("FleetExpansion.MaxShips", cancellationToken);
        if (maxShips > 0 && agent.ShipCount >= maxShips)
        {
            return [];
        }

        var estimate = await capacity.EstimateAsync(cancellationToken);
        if (estimate.IdleShips > 0)
        {
            // Reassign idle ships before recommending expansion.
            return [];
        }

        var decision = await budget.EvaluateAsync(0, cancellationToken);
        if (!decision.CanAfford || decision.SpendableCredits <= 0)
        {
            return [];
        }

        var (shipType, bottleneckKind) = SelectShipTypeForBottleneck(estimate);
        if (shipType is null)
        {
            shipType = await settings.GetAsync<string>("FleetExpansion.PreferredShipType", cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(shipType))
        {
            return [];
        }

        var shipyardWaypoint = await shipyards.FindShipyardForTypeAsync(shipType, cancellationToken);
        if (string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            return [];
        }

        var description = bottleneckKind is not null
            ? $"Expand fleet: bottleneck is {bottleneckKind}; purchasing {shipType} at {shipyardWaypoint}."
            : $"Expand fleet: no idle ships; purchasing {shipType} at {shipyardWaypoint}.";

        return [new FleetGoal(
            Kind: FleetGoalKind.FleetExpansion,
            Description: description,
            Priority: FleetGoalPriority.FleetExpansion,
            EstimatedCost: decision.SpendableCredits,
            ExpansionShipType: shipType,
            ExpansionShipyardWaypoint: shipyardWaypoint)];
    }

    /// <summary>
    /// Determines which ship type best addresses the current fleet bottleneck.
    /// Returns (shipType, bottleneckKind) or (null, null) when no specific bottleneck is detected.
    /// </summary>
    private static (string? ShipType, string? BottleneckKind) SelectShipTypeForBottleneck(FleetCapacityEstimate estimate)
    {
        // Prefer scouting ship when there are no scouts at all
        if (estimate.ScoutingShips == 0 && estimate.TotalShips > 0)
        {
            return (ScoutingShipTypes[0], "MarketCoverage");
        }

        // Mining bottleneck: more haulers than miners, or no miners at all
        if (estimate.MiningShips == 0 || estimate.MiningShips < estimate.HaulingShips)
        {
            return (MiningShipTypes[0], "Extraction");
        }

        // Hauling bottleneck: miners outnumber haulers by more than 1:1
        if (estimate.HaulingShips < estimate.MiningShips)
        {
            return (HaulingShipTypes[0], "Cargo");
        }

        return (null, null);
    }
}

/// <summary>
/// Phase 11g: produces <see cref="FleetGoalKind.MarketScouting"/> goals (priority 10) for every
/// marketplace waypoint whose price data is absent or older than a configurable threshold, so early-game
/// information gathering is handled automatically.
/// </summary>
public sealed class MarketScoutingGoalEvaluator(
    IWaypointRepository waypointRepository,
    IMarketRepository marketRepository,
    IShipGoalRepository shipGoalRepository) : IFleetGoalEvaluator
{
    /// <summary>Default staleness threshold: 3 minutes ≈ 1 SpaceTraders game day.</summary>
    private static readonly TimeSpan DefaultStalenessThreshold = TimeSpan.FromMinutes(3);

    public async Task<IReadOnlyList<FleetGoal>> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var systems = await waypointRepository.GetVisitedSystemSymbolsAsync(cancellationToken);
        if (systems.Count == 0)
        {
            return [];
        }

        var marketWaypoints = new List<string>();
        foreach (var system in systems)
        {
            var waypoints = await waypointRepository.GetBySystemAsync(system, cancellationToken);
            foreach (var w in waypoints.Where(w => w.HasMarket))
            {
                marketWaypoints.Add(w.Symbol);
            }
        }

        if (marketWaypoints.Count == 0)
        {
            return [];
        }

        var freshnessRecords = await marketRepository.GetAllFreshnessAsync(cancellationToken);
        var freshnessByWaypoint = freshnessRecords.ToDictionary(
            r => r.WaypointSymbol,
            r => r.LastObservedAt,
            StringComparer.OrdinalIgnoreCase);

        var activeScoutTargets = await shipGoalRepository.GetActiveScoutTargetsAsync(cancellationToken);

        var now = TimeProvider.System.GetUtcNow();
        var threshold = DefaultStalenessThreshold;

        var goals = new List<FleetGoal>();
        foreach (var waypointSymbol in marketWaypoints)
        {
            if (activeScoutTargets.Contains(waypointSymbol))
            {
                continue;
            }

            var isStale = !freshnessByWaypoint.TryGetValue(waypointSymbol, out var lastObserved)
                || (now - lastObserved) > threshold;

            if (!isStale)
            {
                continue;
            }

            goals.Add(new FleetGoal(
                Kind: FleetGoalKind.MarketScouting,
                Description: $"Scout market at {waypointSymbol} to refresh price data.",
                Priority: FleetGoalPriority.MarketScouting,
                OriginWaypoint: waypointSymbol));
        }

        return goals;
    }
}

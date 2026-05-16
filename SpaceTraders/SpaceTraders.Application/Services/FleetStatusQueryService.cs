using SpaceTraders.Application.Automation;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Concrete implementation of <see cref="IFleetStatusQueryService"/> that assembles
/// <see cref="OrchestratorGoalChain"/> instances by joining active fleet goals with
/// contract / construction data and current ship goal assignments.
/// </summary>
/// <remarks>Phase 15b: replaces the <see cref="StubFleetStatusQueryService"/> stub.</remarks>
internal sealed class FleetStatusQueryService(
    IFleetGoalRepository fleetGoals,
    IShipRepository ships,
    IShipGoalRepository shipGoals,
    IShipAssignmentRepository shipAssignments,
    IContractRepository contracts,
    IConstructionRepository constructions,
    IShipGoalHistoryRepository goalHistory,
    IContractMineralPlanRepository contractPlans) : IFleetStatusQueryService
{
    public async Task<IReadOnlyList<OrchestratorGoalChain>> GetGoalChainsAsync(CancellationToken ct = default)
    {
        var activeGoals = await fleetGoals.GetActiveAsync(ct);
        if (activeGoals.Count == 0)
        {
            var contractPlanChain = await BuildContractPlanChainAsync(ct);
            return contractPlanChain is null ? [] : [contractPlanChain];
        }

        // Build a map of ship symbol → active ship goal for assignment lookup.
        var shipGoalMap = await BuildShipGoalMapAsync(ct);

        var chains = new List<OrchestratorGoalChain>();

        // --- Contract goals: group by ContractId ---
        var contractGroups = activeGoals
            .Where(g => g.Kind == FleetGoalKind.Contract && !string.IsNullOrWhiteSpace(g.ContractId))
            .GroupBy(g => g.ContractId!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(grp => grp.First().Priority);

        foreach (var group in contractGroups)
        {
            var firstGoal = group.OrderBy(g => g.Priority).First();
            var resourceNeeds = new List<ResourceNeedEntry>();
            foreach (var goal in group.OrderBy(g => g.TradeSymbol, StringComparer.OrdinalIgnoreCase))
            {
                resourceNeeds.Add(await BuildContractResourceNeedAsync(goal, shipGoalMap, ct));
            }

            chains.Add(new OrchestratorGoalChain(
                firstGoal.Id,
                FleetGoalKind.Contract,
                firstGoal.Priority,
                $"Contract {group.Key}",
                resourceNeeds));
        }

        // --- Construction goals: group by DestinationWaypoint ---
        var constructionGroups = activeGoals
            .Where(g => g.Kind == FleetGoalKind.Construction && !string.IsNullOrWhiteSpace(g.DestinationWaypoint))
            .GroupBy(g => g.DestinationWaypoint!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(grp => grp.First().Priority);

        foreach (var group in constructionGroups)
        {
            var firstGoal = group.OrderBy(g => g.Priority).First();
            var resourceNeeds = new List<ResourceNeedEntry>();
            foreach (var goal in group.OrderBy(g => g.TradeSymbol, StringComparer.OrdinalIgnoreCase))
            {
                resourceNeeds.Add(await BuildConstructionResourceNeedAsync(goal, shipGoalMap, ct));
            }

            chains.Add(new OrchestratorGoalChain(
                firstGoal.Id,
                FleetGoalKind.Construction,
                firstGoal.Priority,
                $"Supply construction at {group.Key}",
                resourceNeeds));
        }

        // --- All other goals: one chain per goal, no resource breakdown ---
        var otherGoals = activeGoals
            .Where(g => g.Kind != FleetGoalKind.Contract && g.Kind != FleetGoalKind.Construction)
            .OrderBy(g => g.Priority);

        foreach (var goal in otherGoals)
        {
            chains.Add(new OrchestratorGoalChain(
                goal.Id,
                goal.Kind,
                goal.Priority,
                goal.Description,
                []));
        }

        return [.. chains.OrderBy(c => c.Priority)];
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private async Task<Dictionary<string, ShipGoal?>> BuildShipGoalMapAsync(CancellationToken ct)
    {
        var allShips = await ships.GetAllAsync(ct);
        var map = new Dictionary<string, ShipGoal?>(StringComparer.OrdinalIgnoreCase);
        foreach (var ship in allShips)
        {
            map[ship.Symbol] = await shipGoals.GetActiveGoalAsync(ship.Symbol, ct);
        }

        return map;
    }

    private async Task<ResourceNeedEntry> BuildContractResourceNeedAsync(
        FleetGoal goal,
        Dictionary<string, ShipGoal?> shipGoalMap,
        CancellationToken ct)
    {
        // Attempt to look up fulfilled units from the contract repository.
        int unitsNeeded = goal.RemainingUnits;
        int unitsDelivered = 0;
        if (!string.IsNullOrWhiteSpace(goal.ContractId))
        {
            var contract = await contracts.FindAsync(goal.ContractId, ct);
            if (contract?.DeliverablesJson is not null)
            {
                var deliverables = TryDeserializeDeliverables(contract.DeliverablesJson);
                var deliverable = deliverables.FirstOrDefault(d =>
                    string.Equals(d.TradeSymbol, goal.TradeSymbol, StringComparison.OrdinalIgnoreCase));
                if (deliverable is not null)
                {
                    unitsNeeded = deliverable.UnitsRequired;
                    unitsDelivered = deliverable.UnitsFulfilled;
                }
            }
        }

        var assignedShips = FindShipsWithDeliverCargoGoal(
            shipGoalMap,
            goal.ContractId,
            goal.TradeSymbol);

        return new ResourceNeedEntry(
            goal.TradeSymbol ?? string.Empty,
            unitsNeeded,
            unitsDelivered,
            $"needed for contract {goal.ContractId}",
            assignedShips);
    }

    private async Task<ResourceNeedEntry> BuildConstructionResourceNeedAsync(
        FleetGoal goal,
        Dictionary<string, ShipGoal?> shipGoalMap,
        CancellationToken ct)
    {
        // Attempt to look up fulfilled units from the construction repository.
        int unitsNeeded = goal.RemainingUnits;
        int unitsDelivered = 0;
        if (!string.IsNullOrWhiteSpace(goal.DestinationWaypoint))
        {
            var site = await constructions.FindAsync(goal.DestinationWaypoint, ct);
            if (site is not null)
            {
                var material = site.Materials.FirstOrDefault(m =>
                    string.Equals(m.TradeSymbol, goal.TradeSymbol, StringComparison.OrdinalIgnoreCase));
                if (material is not null)
                {
                    unitsNeeded = material.Required;
                    unitsDelivered = material.Fulfilled;
                }
            }
        }

        var assignedShips = FindShipsWithSupplyConstructionGoal(
            shipGoalMap,
            goal.DestinationWaypoint,
            goal.TradeSymbol);

        return new ResourceNeedEntry(
            goal.TradeSymbol ?? string.Empty,
            unitsNeeded,
            unitsDelivered,
            $"needed to build {goal.DestinationWaypoint}",
            assignedShips);
    }

    private static IReadOnlyList<string> FindShipsWithDeliverCargoGoal(
        Dictionary<string, ShipGoal?> shipGoalMap,
        string? contractId,
        string? tradeSymbol)
    {
        if (string.IsNullOrWhiteSpace(contractId) || string.IsNullOrWhiteSpace(tradeSymbol))
        {
            return [];
        }

        return shipGoalMap
            .Where(kvp => kvp.Value is DeliverCargoGoal dlv
                && string.Equals(dlv.ContractId, contractId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(dlv.TradeSymbol, tradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> FindShipsWithSupplyConstructionGoal(
        Dictionary<string, ShipGoal?> shipGoalMap,
        string? waypointSymbol,
        string? tradeSymbol)
    {
        if (string.IsNullOrWhiteSpace(waypointSymbol) || string.IsNullOrWhiteSpace(tradeSymbol))
        {
            return [];
        }

        return shipGoalMap
            .Where(kvp => kvp.Value is SupplyConstructionGoal sup
                && string.Equals(sup.ConstructionSiteWaypointSymbol, waypointSymbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(sup.TradeSymbol, tradeSymbol, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<DTOs.ContractDeliverableDto> TryDeserializeDeliverables(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<DTOs.ContractDeliverableDto>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShipAssignmentSnapshot>> GetAssignmentsAsync(CancellationToken ct = default)
    {
        var allShips = await ships.GetAllAsync(ct);
        if (allShips.Count == 0)
        {
            return [];
        }

        var activeFleetGoals = await fleetGoals.GetActiveAsync(ct);
        var fleetGoalLookup = activeFleetGoals.ToList();
        var activeContractPlan = await contractPlans.GetAsync(ct);

        var snapshots = new List<ShipAssignmentSnapshot>(allShips.Count);
        foreach (var ship in allShips)
        {
            var goal = await shipGoals.GetActiveGoalAsync(ship.Symbol, ct);
            var assignment = await shipAssignments.FindAsync(ship.Symbol, ct);
            var assignedAt = assignment?.AssignedAt;

            var fleetGoal = TryMatchFleetGoal(goal, fleetGoalLookup);
            var snapshot = goal is null && assignment is not null && !assignment.CompletedAt.HasValue
                ? BuildSnapshotFromAssignment(ship.Symbol, assignment, activeContractPlan)
                : BuildSnapshot(ship.Symbol, goal, assignedAt, fleetGoal);
            snapshots.Add(snapshot);
        }

        return snapshots.OrderBy(s => s.ShipSymbol, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<OrchestratorGoalChain?> BuildContractPlanChainAsync(CancellationToken ct)
    {
        var plan = await contractPlans.GetAsync(ct);
        if (plan is null || plan.Status != ContractMineralPlanStatus.Active)
        {
            return null;
        }

        var unitsNeeded = plan.UnitsRequired;
        var unitsDelivered = plan.UnitsFulfilled;

        var contract = await contracts.FindAsync(plan.ContractId, ct);
        if (contract is not null && !string.IsNullOrWhiteSpace(contract.DeliverablesJson))
        {
            var deliverable = TryDeserializeDeliverables(contract.DeliverablesJson)
                .FirstOrDefault(d =>
                    string.Equals(d.TradeSymbol, plan.TradeSymbol, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(d.DestinationSymbol, plan.DestinationWaypoint, StringComparison.OrdinalIgnoreCase));

            if (deliverable is not null)
            {
                unitsNeeded = deliverable.UnitsRequired;
                unitsDelivered = deliverable.UnitsFulfilled;
            }
        }

        var assignedShips = string.IsNullOrWhiteSpace(plan.ShipSymbol)
            ? []
            : new[] { plan.ShipSymbol };

        return new OrchestratorGoalChain(
            FleetGoalId: plan.PlanId,
            FleetGoalKind: FleetGoalKind.Contract,
            Priority: 0,
            FleetGoalDescription: $"Contract {plan.ContractId}",
            ResourceNeeds:
            [
                new ResourceNeedEntry(
                    TradeSymbol: plan.TradeSymbol,
                    UnitsNeeded: unitsNeeded,
                    UnitsDelivered: unitsDelivered,
                    PurposeDescription: $"needed for contract {plan.ContractId}",
                    AssignedShips: assignedShips),
            ]);
    }

    private static ShipAssignmentSnapshot BuildSnapshotFromAssignment(
        string shipSymbol,
        ShipAssignmentDto assignment,
        ContractMineralPlanState? activeContractPlan)
    {
        if (assignment.AssignmentType.Equals("Contract", StringComparison.OrdinalIgnoreCase))
        {
            var source = assignment.OriginWaypoint;
            var destination = assignment.DestWaypoint;
            var tradeSymbol = assignment.CargoSymbol ?? string.Empty;
            var contractId = assignment.ContractId ?? string.Empty;
            var description = string.IsNullOrWhiteSpace(source)
                ? $"Delivering {tradeSymbol} to {destination}"
                : $"Mining {tradeSymbol} at {source}";

            Guid? fleetGoalId = activeContractPlan is not null
                && !string.IsNullOrWhiteSpace(activeContractPlan.ContractId)
                && activeContractPlan.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase)
                && activeContractPlan.TradeSymbol.Equals(tradeSymbol, StringComparison.OrdinalIgnoreCase)
                ? activeContractPlan.PlanId
                : null;

            var fleetGoalDescription = string.IsNullOrWhiteSpace(contractId)
                ? null
                : $"Contract {contractId}";

            return new ShipAssignmentSnapshot(
                ShipSymbol: shipSymbol,
                GoalKind: ShipGoalKind.MineResource,
                GoalDescription: description,
                AssignedAt: assignment.AssignedAt,
                SourceWaypoint: source,
                DestinationWaypoint: destination,
                FleetGoalId: fleetGoalId,
                FleetGoalDescription: fleetGoalDescription);
        }

        return new ShipAssignmentSnapshot(
            ShipSymbol: shipSymbol,
            GoalKind: ShipGoalKind.Idle,
            GoalDescription: "Idle",
            AssignedAt: assignment.AssignedAt,
            SourceWaypoint: assignment.OriginWaypoint,
            DestinationWaypoint: assignment.DestWaypoint,
            FleetGoalId: null,
            FleetGoalDescription: null);
    }

    // -----------------------------------------------------------------------
    // GetAssignmentsAsync helpers
    // -----------------------------------------------------------------------

    private static FleetGoal? TryMatchFleetGoal(ShipGoal? shipGoal, IReadOnlyList<FleetGoal> fleetGoals)
    {
        if (shipGoal is null || fleetGoals.Count == 0)
        {
            return null;
        }

        return shipGoal switch
        {
            DeliverCargoGoal dlv => fleetGoals.FirstOrDefault(fg =>
                fg.Kind == FleetGoalKind.Contract
                && string.Equals(fg.ContractId, dlv.ContractId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fg.TradeSymbol, dlv.TradeSymbol, StringComparison.OrdinalIgnoreCase)),

            SupplyConstructionGoal sup => fleetGoals.FirstOrDefault(fg =>
                fg.Kind == FleetGoalKind.Construction
                && string.Equals(fg.DestinationWaypoint, sup.ConstructionSiteWaypointSymbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(fg.TradeSymbol, sup.TradeSymbol, StringComparison.OrdinalIgnoreCase)),

            ScoutWaypointGoal scout => fleetGoals.FirstOrDefault(fg =>
                (fg.Kind == FleetGoalKind.MarketScouting || fg.Kind == FleetGoalKind.MarketCoverage)
                && string.Equals(fg.OriginWaypoint, scout.TargetWaypointSymbol, StringComparison.OrdinalIgnoreCase)),

            MineResourceGoal mine => fleetGoals.FirstOrDefault(fg =>
                string.Equals(fg.TradeSymbol, mine.TradeSymbol, StringComparison.OrdinalIgnoreCase)),

            SiphonResourceGoal siphon => fleetGoals.FirstOrDefault(fg =>
                string.Equals(fg.TradeSymbol, siphon.TradeSymbol, StringComparison.OrdinalIgnoreCase)),

            _ => null,
        };
    }

    private static ShipAssignmentSnapshot BuildSnapshot(
        string shipSymbol,
        ShipGoal? shipGoal,
        DateTimeOffset? assignedAt,
        FleetGoal? matchedFleetGoal)
    {
        var (goalKind, description, sourceWaypoint, destinationWaypoint) = shipGoal switch
        {
            IdleGoal => (ShipGoalKind.Idle, "Idle", (string?)null, (string?)null),
            MoveToWaypointGoal mv => (ShipGoalKind.MoveToWaypoint, $"Moving to {mv.TargetWaypointSymbol}", null, mv.TargetWaypointSymbol),
            MineResourceGoal mine => (ShipGoalKind.MineResource, $"Mining {mine.TradeSymbol} at {mine.SourceWaypointSymbol}", mine.SourceWaypointSymbol, (string?)null),
            MineAndSellGoal mineAndSell => (ShipGoalKind.MineResource, $"Mining {mineAndSell.TradeSymbol} at {mineAndSell.SourceWaypointSymbol} and selling at {mineAndSell.SellWaypointSymbol}", mineAndSell.SourceWaypointSymbol, mineAndSell.SellWaypointSymbol),
            SiphonResourceGoal siphon => (ShipGoalKind.SiphonResource, $"Siphoning {siphon.TradeSymbol} at {siphon.SourceWaypointSymbol}", siphon.SourceWaypointSymbol, (string?)null),
            SellCargoGoal sell => (ShipGoalKind.SellCargo, $"Selling cargo at {sell.DestinationWaypointSymbol}", null, sell.DestinationWaypointSymbol),
            TradeBetweenMarketsGoal trade => (ShipGoalKind.SellCargo, $"Trading {trade.TradeSymbol} from {trade.BuyWaypointSymbol} to {trade.SellWaypointSymbol}", trade.BuyWaypointSymbol, trade.SellWaypointSymbol),
            DeliverCargoGoal dlv => (ShipGoalKind.DeliverCargo, $"Delivering {dlv.TradeSymbol} to {dlv.DeliveryWaypointSymbol}", null, dlv.DeliveryWaypointSymbol),
            SupplyConstructionGoal sup => (ShipGoalKind.SupplyConstruction, $"Supplying {sup.TradeSymbol} to {sup.ConstructionSiteWaypointSymbol}", null, sup.ConstructionSiteWaypointSymbol),
            ScoutWaypointGoal scout => (ShipGoalKind.ScoutWaypoint, $"Scouting {scout.TargetWaypointSymbol}", null, scout.TargetWaypointSymbol),
            PatrolMarketGoal patrol => (ShipGoalKind.PatrolMarket, $"Patrolling market at {patrol.TargetWaypointSymbol}", null, patrol.TargetWaypointSymbol),
            _ => (ShipGoalKind.Idle, "Idle", null, null),
        };

        return new ShipAssignmentSnapshot(
            ShipSymbol: shipSymbol,
            GoalKind: goalKind,
            GoalDescription: description,
            AssignedAt: assignedAt,
            SourceWaypoint: sourceWaypoint,
            DestinationWaypoint: destinationWaypoint,
            FleetGoalId: matchedFleetGoal?.Id,
            FleetGoalDescription: matchedFleetGoal?.Description);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShipActivitySnapshot>> GetShipActivitiesAsync(CancellationToken ct = default)
    {
        var allShips = await ships.GetAllAsync(ct);
        if (allShips.Count == 0)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var snapshots = new List<ShipActivitySnapshot>(allShips.Count);
        foreach (var ship in allShips)
        {
            var goal = await shipGoals.GetActiveGoalAsync(ship.Symbol, ct);
            snapshots.Add(BuildActivitySnapshot(ship, goal, now));
        }

        return snapshots.OrderBy(s => s.ShipSymbol, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc/>
    public async Task<ShipActivitySnapshot?> GetShipActivityAsync(string shipSymbol, CancellationToken ct = default)
    {
        var ship = await ships.FindAsync(shipSymbol, ct);
        if (ship is null)
        {
            return null;
        }

        var goal = await shipGoals.GetActiveGoalAsync(ship.Symbol, ct);
        return BuildActivitySnapshot(ship, goal, DateTimeOffset.UtcNow);
    }

    // -----------------------------------------------------------------------
    // GetShipActivitiesAsync helpers
    // -----------------------------------------------------------------------

    private static ShipActivitySnapshot BuildActivitySnapshot(
        ShipModel ship,
        ShipGoal? shipGoal,
        DateTimeOffset now)
    {
        var onCooldown = ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now;
        var localStatus = ship.LocalStatus;
        var isInTransit = localStatus == ShipLocalStatus.InTransit;

        string description;
        if (isInTransit && ship.ArrivesAt.HasValue)
        {
            var arrivalTime = ship.ArrivesAt.Value.ToUniversalTime().ToString("HH:mm 'UTC'");
            description = $"Moving to {ship.DestWaypointSymbol ?? "unknown"} (arrives {arrivalTime})";
        }
        else if (onCooldown)
        {
            var tradeSymbol = shipGoal switch
            {
                MineResourceGoal mine => mine.TradeSymbol,
                SiphonResourceGoal siphon => siphon.TradeSymbol,
                _ => null,
            };

            var remainingSeconds = (int)Math.Ceiling((ship.CooldownExpiresAt!.Value - now).TotalSeconds);
            description = tradeSymbol is not null
                ? $"Extracting {tradeSymbol} (cooldown {remainingSeconds} s)"
                : $"On cooldown ({remainingSeconds} s)";
        }
        else if (localStatus == ShipLocalStatus.Docked)
        {
            description = shipGoal switch
            {
                SellCargoGoal sell => $"Docked at {sell.DestinationWaypointSymbol}, selling cargo",
                DeliverCargoGoal dlv => $"Docked at {dlv.DeliveryWaypointSymbol}, delivering cargo",
                SupplyConstructionGoal sup => $"Docked at {sup.ConstructionSiteWaypointSymbol}, supplying construction",
                _ => $"Docked at {ship.WaypointSymbol ?? "unknown"}",
            };
        }
        else if (localStatus == ShipLocalStatus.InOrbit)
        {
            description = shipGoal switch
            {
                MineResourceGoal mine => $"In orbit at {mine.SourceWaypointSymbol}, ready to extract",
                SiphonResourceGoal siphon => $"In orbit at {siphon.SourceWaypointSymbol}, ready to extract",
                ScoutWaypointGoal scout => $"In orbit at {scout.TargetWaypointSymbol}, scouting",
                _ => $"In orbit at {ship.WaypointSymbol ?? "unknown"}",
            };
        }
        else
        {
            description = $"Idle at {ship.WaypointSymbol ?? "unknown"}";
        }

        return new ShipActivitySnapshot(
            ShipSymbol: ship.Symbol,
            LocalStatus: localStatus,
            ActivityDescription: description,
            CargoUsed: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            OnCooldown: onCooldown,
            CurrentWaypoint: isInTransit ? null : ship.WaypointSymbol,
            DestinationWaypoint: isInTransit ? ship.DestWaypointSymbol : null,
            EstimatedArrival: isInTransit ? ship.ArrivesAt : null,
            CooldownExpiresAt: onCooldown ? ship.CooldownExpiresAt : null);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ShipGoalHistoryEntry>?> GetShipGoalHistoryAsync(
        string shipSymbol,
        int limit,
        CancellationToken ct = default)
    {
        var ship = await ships.FindAsync(shipSymbol, ct);
        if (ship is null)
        {
            return null;
        }

        return await goalHistory.GetRecentAsync(shipSymbol, limit, ct);
    }
}

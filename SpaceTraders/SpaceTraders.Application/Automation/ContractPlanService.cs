using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Automation;

public interface IContractPlanService
{
    Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default);

    Task AdvanceAsync(CancellationToken cancellationToken = default);
}

public sealed class ContractPlanService(
    IContractMineralPlanRepository contractPlans,
    IContractRepository contracts,
    IShipRepository ships,
    IShipAssignmentRepository assignments,
    IShipyardRepository shipyards,
    IWaypointRepository waypoints,
    ISpaceTradersPort port,
    IShipPurchaseService shipPurchases,
    IAgentRepository agents,
    IMessageBus bus,
    ILogger<ContractPlanService> logger) : IContractPlanService
{
    private const string ContractAssignmentType = "Contract";
    private const string MinerShipType = "SHIP_MINING_DRONE";

    private static readonly HashSet<string> KnownMineralSymbols =
    [
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
    ];

    public async Task EnsureBootstrappedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await contractPlans.GetAsync(cancellationToken);
        if (existing is not null)
        {
            if (existing.Status == ContractMineralPlanStatus.Active)
            {
                // The tick advances the plan from the cached contract, which deliveries keep current.
                // The events it used to wait for are never published (B9).
                var advanced = await AdvanceActivePlanAsync(existing, cancellationToken);
                if (advanced.Status == ContractMineralPlanStatus.Active)
                {
                    await EnsureActivePlanAssignmentAsync(advanced, cancellationToken);
                }

                return;
            }

            if (existing.Status != ContractMineralPlanStatus.PendingBudget)
            {
                return;
            }

            logger.LogDebug(
                "Contract plan bootstrap: retrying pending-budget plan for contract {ContractId}.",
                existing.ContractId);
        }
        else
        {
            // Only before the first plan: a plan waiting for budget is retried on every tick, and the
            // contract it waits for is cached already (B27).
            await RefreshContractsCacheOnceAsync(cancellationToken);
        }

        var activeContracts = await contracts.GetActiveAsync(cancellationToken);
        if (activeContracts.Count == 0)
        {
            await TryNegotiateContractAsync(cancellationToken);
            activeContracts = await contracts.GetActiveAsync(cancellationToken);
        }

        var pending = SelectPendingDeliverable(activeContracts);
        if (pending is null)
        {
            return;
        }

        if (!pending.IsAccepted)
        {
            var accepted = await port.AcceptContractAsync(pending.ContractId, cancellationToken);
            await contracts.UpsertAsync(MapToDto(accepted), cancellationToken);

            // The acceptance payment: purchases, this plan's drone among them, are budgeted from the
            // cached credits (B33); the ledger and the metrics record it (B7).
            if (accepted.AgentCredits is { } credits)
            {
                await agents.SetCreditsAsync(bus, credits, cancellationToken);
            }

            await bus.PublishAsync(new ContractAcceptedEvent(pending.ContractId, accepted.PaymentOnAccepted));
            activeContracts = await contracts.GetActiveAsync(cancellationToken);
            pending = SelectPendingDeliverable(activeContracts);
            if (pending is null)
            {
                return;
            }
        }

        var now = TimeProvider.System.GetUtcNow();
        var remainingUnits = Math.Max(0, pending.UnitsRequired - pending.UnitsFulfilled);
        if (remainingUnits <= 0)
        {
            return;
        }

        if (!IsMineralSymbol(pending.TradeSymbol))
        {
            await contractPlans.UpsertAsync(new ContractMineralPlanState
            {
                PlanId = Guid.NewGuid(),
                ContractId = pending.ContractId,
                ShipSymbol = string.Empty,
                TradeSymbol = pending.TradeSymbol,
                SourceWaypoint = string.Empty,
                DestinationWaypoint = pending.DestinationSymbol,
                UnitsRequired = pending.UnitsRequired,
                UnitsFulfilled = pending.UnitsFulfilled,
                Status = ContractMineralPlanStatus.DeferredUnsupported,
                CreatedAt = now,
                UpdatedAt = now,
                StopReason = $"Unsupported non-mineral deliverable: {pending.TradeSymbol}.",
            }, cancellationToken);

            logger.LogInformation(
                "Contract plan deferred for contract {ContractId}: unsupported deliverable {TradeSymbol}.",
                pending.ContractId,
                pending.TradeSymbol);
            return;
        }

        var selectedShip = await TrySelectIdleMiningShipAsync(cancellationToken);
        if (selectedShip is null)
        {
            selectedShip = await TryPurchaseMinerDroneAsync(cancellationToken);
        }

        if (selectedShip is null)
        {
            // Retried on every tick: only starting to wait is news, and only that is stored.
            if (existing?.Status == ContractMineralPlanStatus.PendingBudget
                && string.Equals(existing.ContractId, pending.ContractId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug(
                    "Contract plan still pending for contract {ContractId}: no miner available and purchase was not possible.",
                    pending.ContractId);
                return;
            }

            await contractPlans.UpsertAsync(new ContractMineralPlanState
            {
                PlanId = Guid.NewGuid(),
                ContractId = pending.ContractId,
                ShipSymbol = string.Empty,
                TradeSymbol = pending.TradeSymbol,
                SourceWaypoint = string.Empty,
                DestinationWaypoint = pending.DestinationSymbol,
                UnitsRequired = pending.UnitsRequired,
                UnitsFulfilled = pending.UnitsFulfilled,
                Status = ContractMineralPlanStatus.PendingBudget,
                CreatedAt = now,
                UpdatedAt = now,
                StopReason = "No idle mining ship available and unable to purchase SHIP_MINING_DRONE.",
            }, cancellationToken);

            logger.LogInformation(
                "Contract plan pending for contract {ContractId}: no miner available and purchase was not possible.",
                pending.ContractId);
            return;
        }

        var sourceWaypoint = await ResolveSourceAsteroidAsync(selectedShip, pending.TradeSymbol, cancellationToken);
        if (string.IsNullOrWhiteSpace(sourceWaypoint))
        {
            await contractPlans.UpsertAsync(new ContractMineralPlanState
            {
                PlanId = Guid.NewGuid(),
                ContractId = pending.ContractId,
                ShipSymbol = selectedShip.Symbol,
                TradeSymbol = pending.TradeSymbol,
                SourceWaypoint = string.Empty,
                DestinationWaypoint = pending.DestinationSymbol,
                UnitsRequired = pending.UnitsRequired,
                UnitsFulfilled = pending.UnitsFulfilled,
                Status = ContractMineralPlanStatus.DeferredUnsupported,
                CreatedAt = now,
                UpdatedAt = now,
                StopReason = "No asteroid source waypoint found for mineral extraction.",
            }, cancellationToken);

            logger.LogWarning(
                "Contract plan deferred for contract {ContractId}: no asteroid source found for {TradeSymbol}.",
                pending.ContractId,
                pending.TradeSymbol);
            return;
        }

        var plan = new ContractMineralPlanState
        {
            PlanId = Guid.NewGuid(),
            ContractId = pending.ContractId,
            ShipSymbol = selectedShip.Symbol,
            TradeSymbol = pending.TradeSymbol,
            SourceWaypoint = sourceWaypoint,
            DestinationWaypoint = pending.DestinationSymbol,
            UnitsRequired = pending.UnitsRequired,
            UnitsFulfilled = pending.UnitsFulfilled,
            Status = ContractMineralPlanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            StopReason = null,
        };

        await contractPlans.UpsertAsync(plan, cancellationToken);

        await assignments.UpsertAsync(new ShipAssignmentDto(
            ShipSymbol: selectedShip.Symbol,
            AssignmentType: ContractAssignmentType,
            OriginWaypoint: sourceWaypoint,
            DestWaypoint: pending.DestinationSymbol,
            CargoSymbol: pending.TradeSymbol,
            ContractId: pending.ContractId,
            StepIndex: 0,
            AssignedAt: now,
            CompletedAt: null,
            PurchaseUnitPrice: 0,
            RequiredUnits: remainingUnits,
            SupplyCompleted: false), cancellationToken);

        logger.LogInformation(
            "Contract plan bootstrapped: contract {ContractId}, ship {ShipSymbol}, mineral {TradeSymbol}, source {Source}, destination {Destination}, remaining units {RemainingUnits}.",
            pending.ContractId,
            selectedShip.Symbol,
            pending.TradeSymbol,
            sourceWaypoint,
            pending.DestinationSymbol,
            remainingUnits);
    }

    public async Task Handle(DeliverableObtainedEvent @event, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@event.TradeSymbol) || @event.UnitsObtained <= 0)
        {
            return;
        }

        var plan = await contractPlans.GetAsync(cancellationToken);
        if (plan is null || plan.Status != ContractMineralPlanStatus.Active)
        {
            return;
        }

        if (!plan.ShipSymbol.Equals(@event.ShipSymbol, StringComparison.OrdinalIgnoreCase)
            || !plan.TradeSymbol.Equals(@event.TradeSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await AdvanceAsync(cancellationToken);
    }

    public async Task Handle(ContractDeliveryRecordedEvent @event, CancellationToken cancellationToken)
    {
        var plan = await contractPlans.GetAsync(cancellationToken);
        if (plan is null || plan.Status != ContractMineralPlanStatus.Active)
        {
            return;
        }

        if (!plan.ContractId.Equals(@event.ContractId, StringComparison.OrdinalIgnoreCase)
            || !plan.ShipSymbol.Equals(@event.ShipSymbol, StringComparison.OrdinalIgnoreCase)
            || !plan.TradeSymbol.Equals(@event.TradeSymbol, StringComparison.OrdinalIgnoreCase)
            || !plan.DestinationWaypoint.Equals(@event.DestinationWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await AdvanceAsync(cancellationToken);
    }

    public async Task AdvanceAsync(CancellationToken cancellationToken = default)
    {
        var plan = await contractPlans.GetAsync(cancellationToken);
        if (plan is null || plan.Status != ContractMineralPlanStatus.Active)
        {
            return;
        }

        await AdvanceActivePlanAsync(plan, cancellationToken);
    }

    /// <summary>
    /// Brings an active plan up to date with the cached contract, and returns the plan as stored.
    /// It completes, and releases its ship, once the contract is fulfilled. This runs on every tick,
    /// so it writes only what changed.
    /// </summary>
    private async Task<ContractMineralPlanState> AdvanceActivePlanAsync(
        ContractMineralPlanState plan,
        CancellationToken cancellationToken)
    {
        var contract = await contracts.FindAsync(plan.ContractId, cancellationToken);
        if (contract is null)
        {
            logger.LogWarning(
                "Contract plan advance: contract {ContractId} no longer exists in cache.",
                plan.ContractId);
            return plan;
        }

        var now = TimeProvider.System.GetUtcNow();

        if (contract.IsFulfilled)
        {
            var completed = plan with
            {
                Status = ContractMineralPlanStatus.Completed,
                UnitsFulfilled = Math.Max(plan.UnitsFulfilled, plan.UnitsRequired),
                UpdatedAt = now,
                StopReason = null,
            };

            await contractPlans.UpsertAsync(completed, cancellationToken);
            await CompleteAssignmentIfActiveAsync(plan, now, cancellationToken);

            logger.LogInformation(
                "Contract plan completed: contract {ContractId} fulfilled, {Fulfilled}/{Required} {TradeSymbol} delivered; ship {ShipSymbol} released.",
                completed.ContractId,
                completed.UnitsFulfilled,
                completed.UnitsRequired,
                completed.TradeSymbol,
                completed.ShipSymbol);
            return completed;
        }

        var deliverable = DeserializeDeliverables(contract.DeliverablesJson)
            .FirstOrDefault(d =>
                d.TradeSymbol.Equals(plan.TradeSymbol, StringComparison.OrdinalIgnoreCase)
                && d.DestinationSymbol.Equals(plan.DestinationWaypoint, StringComparison.OrdinalIgnoreCase));

        if (deliverable is null)
        {
            logger.LogWarning(
                "Contract plan advance: deliverable {TradeSymbol} -> {Destination} not found for contract {ContractId}.",
                plan.TradeSymbol,
                plan.DestinationWaypoint,
                plan.ContractId);
            return plan;
        }

        // Every unit delivered doesn't complete the plan: the fulfil call, which pays, still has to
        // go out, and the ship's assignment is what sends the ship to make it. Its remaining units
        // drop to 0.
        var remainingUnits = Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled);

        var assignment = await assignments.FindAsync(plan.ShipSymbol, cancellationToken);
        if (assignment is not null
            && !assignment.CompletedAt.HasValue
            && assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(assignment.ContractId, plan.ContractId, StringComparison.OrdinalIgnoreCase)
            && assignment.RequiredUnits != remainingUnits)
        {
            await assignments.UpsertAsync(assignment with { RequiredUnits = remainingUnits }, cancellationToken);
        }

        if (plan.UnitsRequired == deliverable.UnitsRequired && plan.UnitsFulfilled == deliverable.UnitsFulfilled)
        {
            return plan;
        }

        var advanced = plan with
        {
            UnitsRequired = deliverable.UnitsRequired,
            UnitsFulfilled = deliverable.UnitsFulfilled,
            UpdatedAt = now,
        };

        await contractPlans.UpsertAsync(advanced, cancellationToken);
        return advanced;
    }

    private async Task RefreshContractsCacheOnceAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 20;
        var page = 1;

        try
        {
            while (true)
            {
                var response = await port.GetMyContractsAsync(page, pageSize, cancellationToken);
                var items = response?.Items ?? [];

                foreach (var contract in items)
                {
                    await contracts.UpsertAsync(MapToDto(contract), cancellationToken);
                }

                if (items.Count == 0 || page * pageSize >= response!.Total)
                {
                    break;
                }

                page++;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Contract plan: failed to refresh contracts from API; using cached repository state.");
        }
    }

    private async Task TryNegotiateContractAsync(CancellationToken cancellationToken)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);
        var negotiatingShip = allShips
            .Where(s => !string.IsNullOrWhiteSpace(s.WaypointSymbol))
            .OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (negotiatingShip is null)
        {
            return;
        }

        try
        {
            var result = await port.NegotiateContractAsync(negotiatingShip.Symbol, cancellationToken);
            await contracts.UpsertAsync(MapToDto(result.Contract), cancellationToken);

            logger.LogInformation(
                "Contract plan negotiated contract {ContractId} using ship {ShipSymbol}.",
                result.Contract.Id,
                negotiatingShip.Symbol);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Contract plan: negotiate contract failed for ship {ShipSymbol}.", negotiatingShip.Symbol);
        }
    }

    private async Task<ShipModel?> TrySelectIdleMiningShipAsync(CancellationToken cancellationToken)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);
        if (allShips.Count == 0)
        {
            logger.LogDebug("Contract plan ship selection: no ships available in cache.");
            return null;
        }

        var activeAssignments = await assignments.GetAllActiveAsync(cancellationToken);
        var activeAssignmentsByShip = activeAssignments
            .Where(a => !a.CompletedAt.HasValue)
            .ToDictionary(a => a.ShipSymbol, StringComparer.OrdinalIgnoreCase);

        foreach (var ship in allShips.OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            if (activeAssignmentsByShip.TryGetValue(ship.Symbol, out var assignment))
            {
                logger.LogDebug(
                    "Contract plan ship selection: skipping ship {ShipSymbol} because it has active assignment {AssignmentType} (contract {ContractId}).",
                    ship.Symbol,
                    assignment.AssignmentType,
                    assignment.ContractId ?? "<none>");
                continue;
            }

            if (!ship.IsMiningCapable)
            {
                logger.LogDebug(
                    "Contract plan ship selection: skipping ship {ShipSymbol} — not mining-capable (type {ShipType}, mounts: {Mounts}, cargo: {Cargo}, fuel: {Fuel}).",
                    ship.Symbol,
                    ship.ShipType,
                    string.Join(",", ship.MountSymbols ?? []),
                    ship.CargoCapacity,
                    ship.FuelCapacity);
                continue;
            }

            logger.LogDebug(
                "Contract plan ship selection: selected idle mining-capable ship {ShipSymbol}.",
                ship.Symbol);
            return ship;
        }

        logger.LogDebug(
            "Contract plan ship selection: no eligible idle mining-capable ship found after evaluating {ShipCount} ships.",
            allShips.Count);

        return null;
    }

    private async Task<ShipModel?> TryPurchaseMinerDroneAsync(CancellationToken cancellationToken)
    {
        var shipyardWaypoint = await shipyards.FindShipyardForTypeAsync(MinerShipType, cancellationToken);
        if (string.IsNullOrWhiteSpace(shipyardWaypoint))
        {
            logger.LogDebug(
                "Contract plan purchase fallback: no shipyard waypoint found for ship type {ShipType}.",
                MinerShipType);
            return null;
        }

        var purchased = await shipPurchases.TryPurchaseAsync(MinerShipType, shipyardWaypoint, cancellationToken);
        if (!purchased.IsSuccess || purchased.PurchasedShip is null)
        {
            logger.LogDebug(
                "Contract plan purchase fallback: purchase denied for {ShipType} at {WaypointSymbol} - {Reason}",
                MinerShipType,
                shipyardWaypoint,
                purchased.FailureReason ?? "Purchase failed.");
            return null;
        }

        logger.LogInformation(
            "Contract plan purchased new miner drone {ShipSymbol} at {ShipyardWaypoint} for contract work.",
            purchased.PurchasedShip.Symbol,
            shipyardWaypoint);

        return purchased.PurchasedShip;
    }

    public async Task<string> ResolveSourceAsteroidAsync(ShipModel ship, string tradeSymbol, CancellationToken cancellationToken)
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

    private static PendingDeliverable? SelectPendingDeliverable(IReadOnlyList<ContractDto> activeContracts)
    {
        var selectedContract = activeContracts
            .Where(c => !c.IsFulfilled)
            .OrderBy(c => c.TermsDeadline ?? c.Expiration ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();

        if (selectedContract is null)
        {
            return null;
        }

        var deliverable = DeserializeDeliverables(selectedContract.DeliverablesJson)
            .FirstOrDefault(d => d.UnitsRequired > d.UnitsFulfilled);

        if (deliverable is null)
        {
            return null;
        }

        return new PendingDeliverable(
            selectedContract.Id,
            selectedContract.IsAccepted,
            deliverable.TradeSymbol,
            deliverable.DestinationSymbol,
            deliverable.UnitsRequired,
            deliverable.UnitsFulfilled);
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

    private async Task EnsureActivePlanAssignmentAsync(ContractMineralPlanState plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plan.ShipSymbol)
            || string.IsNullOrWhiteSpace(plan.ContractId)
            || string.IsNullOrWhiteSpace(plan.TradeSymbol)
            || string.IsNullOrWhiteSpace(plan.SourceWaypoint)
            || string.IsNullOrWhiteSpace(plan.DestinationWaypoint))
        {
            return;
        }

        var contract = await contracts.FindAsync(plan.ContractId, cancellationToken);
        if (contract is null || contract.IsFulfilled)
        {
            return;
        }

        var deliverable = DeserializeDeliverables(contract.DeliverablesJson)
            .FirstOrDefault(d =>
                d.TradeSymbol.Equals(plan.TradeSymbol, StringComparison.OrdinalIgnoreCase)
                && d.DestinationSymbol.Equals(plan.DestinationWaypoint, StringComparison.OrdinalIgnoreCase));

        if (deliverable is null)
        {
            return;
        }

        var remainingUnits = Math.Max(0, deliverable.UnitsRequired - deliverable.UnitsFulfilled);
        if (remainingUnits <= 0)
        {
            return;
        }

        var assignment = await assignments.FindAsync(plan.ShipSymbol, cancellationToken);
        var shouldCreate = assignment is null
            || assignment.CompletedAt.HasValue
            || !assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.ContractId, plan.ContractId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.CargoSymbol, plan.TradeSymbol, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.OriginWaypoint, plan.SourceWaypoint, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.DestWaypoint, plan.DestinationWaypoint, StringComparison.OrdinalIgnoreCase);

        if (shouldCreate)
        {
            var now = TimeProvider.System.GetUtcNow();
            await assignments.UpsertAsync(new ShipAssignmentDto(
                ShipSymbol: plan.ShipSymbol,
                AssignmentType: ContractAssignmentType,
                OriginWaypoint: plan.SourceWaypoint,
                DestWaypoint: plan.DestinationWaypoint,
                CargoSymbol: plan.TradeSymbol,
                ContractId: plan.ContractId,
                StepIndex: 0,
                AssignedAt: now,
                CompletedAt: null,
                PurchaseUnitPrice: 0,
                RequiredUnits: remainingUnits,
                SupplyCompleted: false), cancellationToken);

            logger.LogInformation(
                "Contract plan bootstrap: restored missing active assignment for contract {ContractId} on ship {ShipSymbol}.",
                plan.ContractId,
                plan.ShipSymbol);
        }

        // An existing assignment's remaining units are kept current by AdvanceActivePlanAsync.
    }

    private static bool IsMineralSymbol(string tradeSymbol)
    {
        if (string.IsNullOrWhiteSpace(tradeSymbol))
        {
            return false;
        }

        if (KnownMineralSymbols.Contains(tradeSymbol))
        {
            return true;
        }

        return tradeSymbol.EndsWith("_ORE", StringComparison.OrdinalIgnoreCase)
            || tradeSymbol.EndsWith("_CRYSTALS", StringComparison.OrdinalIgnoreCase)
            || tradeSymbol.EndsWith("_ICE", StringComparison.OrdinalIgnoreCase)
            || tradeSymbol.EndsWith("_SAND", StringComparison.OrdinalIgnoreCase);
    }

    private static ContractDto MapToDto(ContractModel contract)
    {
        return new ContractDto(
            contract.Id,
            contract.FactionSymbol,
            contract.Type,
            contract.IsAccepted,
            contract.IsFulfilled,
            contract.Expiration,
            contract.DeadlineToAccept,
            contract.TermsDeadline,
            JsonSerializer.Serialize(contract.Deliverables.Select(d =>
                new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList()));
    }

    private static ContractDto MapToDto(ContractActionResult result)
    {
        return new ContractDto(
            result.ContractId,
            result.FactionSymbol,
            result.ContractType,
            result.IsAccepted,
            result.IsFulfilled,
            result.Expiration,
            result.DeadlineToAccept,
            result.TermsDeadline,
            JsonSerializer.Serialize(result.Deliverables.Select(d =>
                new ContractDeliverableDto(d.TradeSymbol, d.DestinationSymbol, d.UnitsRequired, d.UnitsFulfilled)).ToList()));
    }

    private async Task CompleteAssignmentIfActiveAsync(
        ContractMineralPlanState plan,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(plan.ShipSymbol, cancellationToken);
        if (assignment is null
            || assignment.CompletedAt.HasValue
            || !assignment.AssignmentType.Equals(ContractAssignmentType, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(assignment.ContractId, plan.ContractId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await assignments.UpsertAsync(assignment with { CompletedAt = completedAt }, cancellationToken);
    }

    private sealed record PendingDeliverable(
        string ContractId,
        bool IsAccepted,
        string TradeSymbol,
        string DestinationSymbol,
        int UnitsRequired,
        int UnitsFulfilled);

    private sealed class LegacyContractShipPurchaseService(
        ISpaceTradersPort port,
        IAgentRepository agents,
        IShipRepository ships,
        IShipyardRepository shipyards,
        ILogger<LegacyContractShipPurchaseService> logger) : IShipPurchaseService
    {
        public async Task<ShipPurchaseResult> TryPurchaseAsync(
            string shipType,
            string shipyardWaypoint,
            CancellationToken cancellationToken = default)
        {
            var shipyard = await shipyards.FindByWaypointAsync(shipyardWaypoint, cancellationToken);
            var price = shipyard?.Ships
                .FirstOrDefault(s => s.Type.Equals(shipType, StringComparison.OrdinalIgnoreCase))?
                .PurchasePrice ?? 0;

            if (price <= 0)
            {
                return new ShipPurchaseResult
                {
                    IsSuccess = false,
                    FailureReason = "Purchase price unknown.",
                    EstimatedCost = price,
                };
            }

            var agent = await agents.GetAsync(cancellationToken);
            if (agent is null || agent.Credits < price)
            {
                return new ShipPurchaseResult
                {
                    IsSuccess = false,
                    FailureReason = "Insufficient credits.",
                    EstimatedCost = price,
                };
            }

            var purchased = await port.PurchaseShipAsync(shipType, shipyardWaypoint, cancellationToken);
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
                ShipType: shipType,
                CargoInventory: purchased.ShipCargo.Inventory);

            await ships.UpsertAsync(ship, cancellationToken);

            logger.LogInformation(
                "LegacyContractShipPurchaseService: purchased {ShipSymbol} at {Shipyard}.",
                ship.Symbol,
                shipyardWaypoint);

            return new ShipPurchaseResult
            {
                IsSuccess = true,
                EstimatedCost = price,
                ActualCost = purchased.Cost,
                PurchasedShip = ship,
            };
        }
    }
}

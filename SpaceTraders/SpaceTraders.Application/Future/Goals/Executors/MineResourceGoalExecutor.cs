using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using System.Text.Json;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="MineResourceGoal"/>.
/// Handles the full mine → sell cycle from any starting state.
/// Phase 12b: capability validation delegated to <see cref="IShipCapabilityRegistry"/>.
/// </summary>
public sealed class MineResourceGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus,
    IShipCapabilityRegistry capabilityRegistry) : IShipGoalExecutor
{
    private const decimal CriticalFuelRatio = 0.15m;

    public bool CanExecute(ShipGoal goal) => goal is MineResourceGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var mineGoal = (MineResourceGoal)goal;
        var caps = capabilityRegistry.GetCapabilities(ship);

        if (!caps.CanMine)
        {
            return GoalExecutionResult.Blocked("Ship has no mining equipment (no mining mount or miner frame).");
        }

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            // 1. Deliver contract cargo at the current docked waypoint.
            var deliverDecision = TryContractDelivery(ship, ctx);
            if (deliverDecision is not null)
            {
                await dockedCommands.DeliverContractAsync(
                    deliverDecision.Value.ContractId,
                    ship.Symbol,
                    deliverDecision.Value.TradeSymbol,
                    deliverDecision.Value.Units,
                    ship.WaypointSymbol ?? string.Empty,
                    ct);
            }

            // 2. Sell eligible non-protected cargo.
            var sellDecision = TrySellCargo(ship, ctx);
            if (sellDecision is not null)
            {
                await dockedCommands.SellCargoAsync(ship.Symbol, sellDecision.Value.TradeSymbol, sellDecision.Value.Units, ct);
            }

            // 3. Jettison low-value cargo when full.
            var jettisonDecision = TryJettisonCargo(ship, ctx);
            if (jettisonDecision is not null)
            {
                await bus.InvokeAsync(new JettisonCargoCommand(ship.Symbol, jettisonDecision.Value.TradeSymbol, jettisonDecision.Value.Units), ct);
            }

            // 4. Refuel from hydrocarbon cargo if needed.
            if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity)
            {
                var hydroUnits = ship.CargoInventory?
                    .FirstOrDefault(c => c.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase))?
                    .Units ?? 0;
                if (hydroUnits > ctx.MiningReserveHydrocarbonUnits)
                {
                    await dockedCommands.RefuelAsync(ship.Symbol, fromCargo: true, ct);
                }
            }

            // 5. Refuel from market if needed.
            if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity && ctx.CurrentWaypointSellsFuel)
            {
                await dockedCommands.RefuelAsync(ship.Symbol, fromCargo: false, ct);
            }

            // 6. Orbit and continue.
            await dockedCommands.OrbitAsync(ship.Symbol, ct);
        }
        else if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        // Effectively in orbit now.
        var atSource = string.Equals(ship.WaypointSymbol, mineGoal.SourceWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (atSource)
        {
            return await PlanAtSourceAsync(ship, mineGoal, ctx, caps, ct);
        }

        return await PlanNavigatingToSourceAsync(ship, mineGoal, ctx, ct);
    }

    private async Task<GoalExecutionResult> PlanAtSourceAsync(ShipModel ship, MineResourceGoal goal, ShipGoalContext ctx, ShipCapabilities caps, CancellationToken ct)
    {
        // Check cooldown.
        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return GoalExecutionResult.WaitingForCooldown("Ship is on cooldown.", ship.CooldownExpiresAt);
        }

        // Cargo full: navigate to nearest sell market.
        if (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            var sellMarket = ctx.NearestSellMarket ?? ctx.FuelMarketWaypoint;
            if (string.IsNullOrWhiteSpace(sellMarket))
            {
                return GoalExecutionResult.Blocked("Cargo is full and no sell market is known.");
            }

            await inOrbitCommands.NavigateAsync(ship.Symbol, sellMarket, ct);
            return GoalExecutionResult.WaitingForArrival($"Cargo full; navigating to sell at {sellMarket}.");
        }

        // Survey if needed.
        if (caps.CanSurvey && ctx.ActiveSurveyCount == 0)
        {
            await inOrbitCommands.SurveyAsync(ship.Symbol, ct);
            return GoalExecutionResult.WaitingForCooldown("Surveying before extraction.");
        }

        // Extract.
        await inOrbitCommands.ExtractAsync(ship.Symbol, ct);
        return GoalExecutionResult.WaitingForCooldown("Extracting resources.");
    }

    private async Task<GoalExecutionResult> PlanNavigatingToSourceAsync(ShipModel ship, MineResourceGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        // Low fuel: navigate to fuel market.
        if (ship.FuelCapacity > 0)
        {
            var fuelRatio = (decimal)ship.FuelCurrent / ship.FuelCapacity;
            if (fuelRatio <= CriticalFuelRatio && !string.IsNullOrWhiteSpace(ctx.FuelMarketWaypoint))
            {
                if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
                {
                    await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
                }

                if (string.Equals(ship.WaypointSymbol, ctx.FuelMarketWaypoint, StringComparison.OrdinalIgnoreCase))
                {
                    // Already at fuel market in orbit — dock and navigate to source on next execution.
                    // Dock is instant; the orchestrator will re-evaluate after docking is complete.
                    await inOrbitCommands.DockAsync(ship.Symbol, ct);
                    return GoalExecutionResult.WaitingForArrival("At fuel market; docked to refuel.");
                }

                await inOrbitCommands.NavigateAsync(ship.Symbol, ctx.FuelMarketWaypoint, ct);
                return GoalExecutionResult.WaitingForArrival("Critically low fuel; navigating to fuel market.");
            }
        }

        // No fuel tank: drift.
        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
        }
        else if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            // Patch flight mode if needed.
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, goal.SourceWaypointSymbol, ct);
        return GoalExecutionResult.WaitingForArrival($"Navigating to source {goal.SourceWaypointSymbol}.");
    }

    private static (string ContractId, string TradeSymbol, int Units)? TryContractDelivery(ShipModel ship, ShipGoalContext ctx)
    {
        if (ship.CargoInventory is null || string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return null;
        }

        foreach (var contract in ctx.ActiveContracts.Where(c => c.IsAccepted && !c.IsFulfilled))
        {
            var deliverables = DeserializeDeliverables(contract.DeliverablesJson);
            foreach (var deliverable in deliverables)
            {
                if (!deliverable.DestinationSymbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase)
                    || deliverable.UnitsRequired <= deliverable.UnitsFulfilled)
                {
                    continue;
                }

                var cargoUnits = ship.CargoInventory
                    .FirstOrDefault(i => i.Symbol.Equals(deliverable.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
                    .Units ?? 0;
                if (cargoUnits <= 0) continue;

                var unitsToDeliver = Math.Min(cargoUnits, deliverable.UnitsRequired - deliverable.UnitsFulfilled);
                if (unitsToDeliver > 0)
                {
                    return (contract.Id, deliverable.TradeSymbol, unitsToDeliver);
                }
            }
        }
        return null;
    }

    private static (string TradeSymbol, int Units)? TrySellCargo(ShipModel ship, ShipGoalContext ctx)
    {
        if (ship.CargoInventory is null) return null;

        var protectedSymbols = GetProtectedCargo(ctx);
        foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
        {
            if (protectedSymbols.Contains(cargo.Symbol)) continue;

            var isHydro = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase);
            if (isHydro && cargo.Units <= ctx.MiningReserveHydrocarbonUnits) continue;

            var sellPrice = ctx.CurrentMarketSnapshot?.TradeGoods
                .FirstOrDefault(g => g.Symbol.Equals(cargo.Symbol, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0;

            if (sellPrice > 0 && sellPrice >= ctx.MiningMinimumSellPrice)
            {
                var units = isHydro
                    ? Math.Max(0, cargo.Units - ctx.MiningReserveHydrocarbonUnits)
                    : cargo.Units;
                if (units > 0) return (cargo.Symbol, units);
            }
        }
        return null;
    }

    private static (string TradeSymbol, int Units)? TryJettisonCargo(ShipModel ship, ShipGoalContext ctx)
    {
        if (!ctx.MiningJettisonLowValueWhenFull) return null;
        if (ship.CargoCurrent < ship.CargoCapacity) return null;
        if (ship.CargoInventory is null) return null;

        var protectedSymbols = GetProtectedCargo(ctx);
        foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
        {
            if (protectedSymbols.Contains(cargo.Symbol)) continue;

            var isHydro = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase);
            var units = isHydro
                ? Math.Max(0, cargo.Units - ctx.MiningReserveHydrocarbonUnits)
                : cargo.Units;
            if (units > 0) return (cargo.Symbol, units);
        }
        return null;
    }

    private static HashSet<string> GetProtectedCargo(ShipGoalContext ctx)
    {
        var protectedCargo = ctx.ActiveContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .SelectMany(c => DeserializeDeliverables(c.DeliverablesJson))
            .Where(d => d.UnitsRequired > d.UnitsFulfilled)
            .Select(d => d.TradeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in ctx.ProtectedCargoSymbols)
        {
            protectedCargo.Add(symbol);
        }

        return protectedCargo;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}

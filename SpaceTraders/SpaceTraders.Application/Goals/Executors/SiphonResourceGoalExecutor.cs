using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using System.Text.Json;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="SiphonResourceGoal"/>.
/// Handles the full siphon → sell cycle from any starting state.
/// </summary>
public sealed class SiphonResourceGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IShipGoalExecutor
{
    private const decimal CriticalFuelRatio = 0.15m;

    public bool CanExecute(ShipGoal goal) => goal is SiphonResourceGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var siphonGoal = (SiphonResourceGoal)goal;

        if (!ship.HasGasSiphonEquipment)
        {
            return GoalExecutionResult.Blocked("Ship has no gas siphon equipment.");
        }

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return await PlanDockedAsync(ship, ctx, ct);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        var atSource = string.Equals(ship.WaypointSymbol, siphonGoal.SourceWaypointSymbol, StringComparison.OrdinalIgnoreCase);
        return atSource
            ? await PlanAtSourceAsync(ship, siphonGoal, ctx, ct)
            : await PlanNavigatingToSourceAsync(ship, siphonGoal, ctx, ct);
    }

    private async Task<GoalExecutionResult> PlanDockedAsync(ShipModel ship, ShipGoalContext ctx, CancellationToken ct)
    {
        var sellDecision = TrySellCargo(ship, ctx);
        if (sellDecision is not null)
        {
            await dockedCommands.SellCargoAsync(ship.Symbol, sellDecision.Value.TradeSymbol, sellDecision.Value.Units, ct);
            return GoalExecutionResult.Progressing($"Selling {sellDecision.Value.Units}x {sellDecision.Value.TradeSymbol}.");
        }

        if (ship.FuelCapacity > 0 && ship.FuelCurrent < ship.FuelCapacity && ctx.CurrentWaypointSellsFuel)
        {
            await dockedCommands.RefuelAsync(ship.Symbol, fromCargo: false, ct);
            return GoalExecutionResult.Progressing("Refueling at market.");
        }

        await dockedCommands.OrbitAsync(ship.Symbol, ct);
        return GoalExecutionResult.Progressing("Orbiting to continue siphon cycle.");
    }

    private async Task<GoalExecutionResult> PlanAtSourceAsync(ShipModel ship, SiphonResourceGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return GoalExecutionResult.WaitingForCooldown("Ship is on cooldown.", ship.CooldownExpiresAt);
        }

        if (ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity)
        {
            var sellMarket = ctx.NearestSellMarket ?? ctx.FuelMarketWaypoint;
            if (string.IsNullOrWhiteSpace(sellMarket))
            {
                return GoalExecutionResult.Blocked("Cargo is full and no sell market is known.");
            }
            await inOrbitCommands.NavigateAsync(ship.Symbol, sellMarket, ct);
            return GoalExecutionResult.Progressing($"Cargo full; navigating to sell at {sellMarket}.");
        }

        await inOrbitCommands.SiphonAsync(ship.Symbol, ct);
        return GoalExecutionResult.Progressing("Siphoning gas resources.");
    }

    private async Task<GoalExecutionResult> PlanNavigatingToSourceAsync(ShipModel ship, SiphonResourceGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        if (ship.FuelCapacity > 0)
        {
            var fuelRatio = (decimal)ship.FuelCurrent / ship.FuelCapacity;
            if (fuelRatio <= CriticalFuelRatio && !string.IsNullOrWhiteSpace(ctx.FuelMarketWaypoint))
            {
                if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
                {
                    await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
                    return GoalExecutionResult.Progressing("Critically low fuel; switching to DRIFT.");
                }
                await inOrbitCommands.NavigateAsync(ship.Symbol, ctx.FuelMarketWaypoint, ct);
                return GoalExecutionResult.Progressing("Critically low fuel; navigating to fuel market.");
            }
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
            return GoalExecutionResult.Progressing("No fuel tank; switching to DRIFT.");
        }

        if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
            return GoalExecutionResult.Progressing($"Adjusting flight mode to {ctx.RecommendedFlightMode}.");
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, goal.SourceWaypointSymbol, ct);
        return GoalExecutionResult.Progressing($"Navigating to source {goal.SourceWaypointSymbol}.");
    }

    private static (string TradeSymbol, int Units)? TrySellCargo(ShipModel ship, ShipGoalContext ctx)
    {
        if (ship.CargoInventory is null) return null;

        var protectedSymbols = ctx.ActiveContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .SelectMany(c => DeserializeDeliverables(c.DeliverablesJson))
            .Where(d => d.UnitsRequired > d.UnitsFulfilled)
            .Select(d => d.TradeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
        {
            if (protectedSymbols.Contains(cargo.Symbol)) continue;

            var isHydro = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase);
            if (isHydro && cargo.Units <= ctx.MiningReserveHydrocarbonUnits) continue;

            var sellPrice = ctx.CurrentMarketSnapshot?.TradeGoods
                .FirstOrDefault(g => g.Symbol.Equals(cargo.Symbol, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0;

            if (sellPrice > 0 && sellPrice >= ctx.MiningMinimumSellPrice)
            {
                var units = isHydro ? Math.Max(0, cargo.Units - ctx.MiningReserveHydrocarbonUnits) : cargo.Units;
                if (units > 0) return (cargo.Symbol, units);
            }
        }
        return null;
    }

    private static IReadOnlyList<ContractDeliverableDto> DeserializeDeliverables(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<ContractDeliverableDto>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}

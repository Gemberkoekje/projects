using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="SupplyConstructionGoal"/>.
/// Navigates to the construction site and supplies the required trade good.
/// </summary>
public sealed class SupplyConstructionGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IShipGoalExecutor
{
    private const string FabMatsSymbol = "FAB_MATS";
    private const string FabMatSymbol = "FAB_MAT";

    public bool CanExecute(ShipGoal goal) => goal is SupplyConstructionGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var supplyGoal = (SupplyConstructionGoal)goal;

        if (ctx.ConstructionComplete)
        {
            return GoalExecutionResult.Completed("Construction site is already complete.");
        }

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        var atSite = string.Equals(ship.WaypointSymbol, supplyGoal.ConstructionSiteWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (!atSite && ShouldBulkBuyFabMats(ship, supplyGoal, ctx, out var unitsToBuy))
            {
                await dockedCommands.BuyCargoAsync(ship.Symbol, supplyGoal.TradeSymbol, unitsToBuy, ct);
                return GoalExecutionResult.Progressing($"Buying {unitsToBuy}x {supplyGoal.TradeSymbol} before construction delivery.");
            }

            await dockedCommands.OrbitAsync(ship.Symbol, ct);
        }
        else if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        // Effectively in orbit now.
        if (atSite)
        {
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(c => c.Symbol.Equals(supplyGoal.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            if (cargoUnits <= 0)
            {
                return GoalExecutionResult.Completed("No cargo to supply; supply complete.");
            }

            var systemSymbol = ship.SystemSymbol ?? string.Empty;
            await inOrbitCommands.SupplyConstructionAsync(
                ship.Symbol,
                systemSymbol,
                supplyGoal.ConstructionSiteWaypointSymbol,
                supplyGoal.TradeSymbol,
                cargoUnits,
                Guid.NewGuid(),
                Guid.NewGuid(),
                ct);
            return GoalExecutionResult.Completed($"Supplied {cargoUnits}x {supplyGoal.TradeSymbol} to construction site.");
        }

        if (ship.FuelCapacity <= 0 && !string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, "DRIFT"), ct);
        }
        else if (ship.FuelCapacity > 0
            && !string.IsNullOrWhiteSpace(ctx.RecommendedFlightMode)
            && !string.Equals(ship.FlightMode, ctx.RecommendedFlightMode, StringComparison.OrdinalIgnoreCase))
        {
            await bus.InvokeAsync(new PatchShipNavCommand(ship.Symbol, ctx.RecommendedFlightMode), ct);
        }

        await inOrbitCommands.NavigateAsync(ship.Symbol, supplyGoal.ConstructionSiteWaypointSymbol, ct);
        return GoalExecutionResult.WaitingForArrival($"Navigating to construction site {supplyGoal.ConstructionSiteWaypointSymbol}.");
    }

    private static bool ShouldBulkBuyFabMats(ShipModel ship, SupplyConstructionGoal goal, ShipGoalContext ctx, out int unitsToBuy)
    {
        unitsToBuy = 0;
        if (!goal.TradeSymbol.Equals(FabMatsSymbol, StringComparison.OrdinalIgnoreCase)
            && !goal.TradeSymbol.Equals(FabMatSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ctx.HourlyConstructionBudgetCapEnabled)
        {
            return false;
        }

        var marketGood = ctx.CurrentMarketSnapshot?.TradeGoods.FirstOrDefault(g =>
            g.Symbol.Equals(goal.TradeSymbol, StringComparison.OrdinalIgnoreCase));
        if (marketGood is null || marketGood.PurchasePrice <= 0 || marketGood.PurchasePrice > ctx.FabMatsBuyThreshold)
        {
            return false;
        }

        var cargoFree = Math.Max(0, ship.CargoCapacity - ship.CargoCurrent);
        if (cargoFree <= 0 || ctx.SpendableCredits <= 0)
        {
            return false;
        }

        var affordableUnits = (int)Math.Min(int.MaxValue, ctx.SpendableCredits / marketGood.PurchasePrice);
        if (affordableUnits <= 0)
        {
            return false;
        }

        unitsToBuy = Math.Min(cargoFree, Math.Min(ctx.FabMatsTransactionSize, affordableUnits));
        return unitsToBuy > 0;
    }
}

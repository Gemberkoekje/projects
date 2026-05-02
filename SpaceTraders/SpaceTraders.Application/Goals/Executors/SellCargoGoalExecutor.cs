using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="SellCargoGoal"/>.
/// Navigates to the destination and sells all listed trade goods.
/// </summary>
public sealed class SellCargoGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is SellCargoGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var sellGoal = (SellCargoGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        var atDest = string.Equals(ship.WaypointSymbol, sellGoal.DestinationWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (!atDest)
            {
                await dockedCommands.OrbitAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing("Orbiting to navigate to sell destination.");
            }

            // At destination: sell the next unsold trade good.
            if (ship.CargoInventory is not null)
            {
                foreach (var symbol in sellGoal.TradeSymbols)
                {
                    var cargo = ship.CargoInventory.FirstOrDefault(c =>
                        c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) && c.Units > 0);
                    if (cargo is null) continue;

                    var sellPrice = ctx.CurrentMarketSnapshot?.TradeGoods
                        .FirstOrDefault(g => g.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0;
                    if (sellPrice > 0)
                    {
                        await dockedCommands.SellCargoAsync(ship.Symbol, cargo.Symbol, cargo.Units, ct);
                        return GoalExecutionResult.Progressing($"Selling {cargo.Units}x {cargo.Symbol}.");
                    }
                }
            }

            return GoalExecutionResult.Completed("All listed cargo has been sold.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        if (atDest)
        {
            await inOrbitCommands.DockAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing("At sell destination; docking.");
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

        await inOrbitCommands.NavigateAsync(ship.Symbol, sellGoal.DestinationWaypointSymbol, ct);
        return GoalExecutionResult.Progressing($"Navigating to sell destination {sellGoal.DestinationWaypointSymbol}.");
    }
}

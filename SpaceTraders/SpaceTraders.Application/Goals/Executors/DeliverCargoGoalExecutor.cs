using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using Wolverine;

namespace SpaceTraders.Application.Goals.Executors;

/// <summary>
/// Phase 10: executor for <see cref="DeliverCargoGoal"/>.
/// Navigates to the delivery waypoint and delivers contract cargo.
/// </summary>
public sealed class DeliverCargoGoalExecutor(
    IInOrbitCommandAcceptor inOrbitCommands,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IShipGoalExecutor
{
    public bool CanExecute(ShipGoal goal) => goal is DeliverCargoGoal;

    public async Task<GoalExecutionResult> ExecuteStepAsync(ShipModel ship, ShipGoal goal, ShipGoalContext ctx, CancellationToken ct)
    {
        var deliverGoal = (DeliverCargoGoal)goal;

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return GoalExecutionResult.WaitingForArrival("Ship is in transit.");
        }

        var atDest = string.Equals(ship.WaypointSymbol, deliverGoal.DeliveryWaypointSymbol, StringComparison.OrdinalIgnoreCase);

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            if (!atDest)
            {
                await dockedCommands.OrbitAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing("Orbiting to navigate to delivery waypoint.");
            }

            // At delivery waypoint: deliver.
            var cargoUnits = ship.CargoInventory?
                .FirstOrDefault(c => c.Symbol.Equals(deliverGoal.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
                .Units ?? 0;

            if (cargoUnits <= 0)
            {
                return GoalExecutionResult.Completed("No cargo to deliver; delivery complete.");
            }

            await dockedCommands.DeliverContractAsync(
                deliverGoal.ContractId,
                ship.Symbol,
                deliverGoal.TradeSymbol,
                cargoUnits,
                deliverGoal.DeliveryWaypointSymbol,
                ct);
            return GoalExecutionResult.Progressing($"Delivering {cargoUnits}x {deliverGoal.TradeSymbol}.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

        if (atDest)
        {
            await inOrbitCommands.DockAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing("At delivery waypoint; docking.");
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

        await inOrbitCommands.NavigateAsync(ship.Symbol, deliverGoal.DeliveryWaypointSymbol, ct);
        return GoalExecutionResult.Progressing($"Navigating to delivery waypoint {deliverGoal.DeliveryWaypointSymbol}.");
    }
}

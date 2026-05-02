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
            if (!atSite)
            {
                await dockedCommands.OrbitAsync(ship.Symbol, ct);
                return GoalExecutionResult.Progressing("Orbiting to navigate to construction site.");
            }

            // At site: orbit to supply (supply requires in-orbit).
            await dockedCommands.OrbitAsync(ship.Symbol, ct);
            return GoalExecutionResult.Progressing("Orbiting at construction site to supply.");
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return GoalExecutionResult.Blocked($"Unexpected ship status: {ship.Status}.");
        }

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
            return GoalExecutionResult.Progressing($"Supplying {cargoUnits}x {supplyGoal.TradeSymbol} to construction site.");
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

        await inOrbitCommands.NavigateAsync(ship.Symbol, supplyGoal.ConstructionSiteWaypointSymbol, ct);
        return GoalExecutionResult.Progressing($"Navigating to construction site {supplyGoal.ConstructionSiteWaypointSymbol}.");
    }
}

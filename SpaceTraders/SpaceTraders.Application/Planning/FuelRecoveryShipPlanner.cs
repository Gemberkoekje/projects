using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4b: cross-cutting fuel-recovery planner migrated out of
/// <c>ShipInOrbitFuelRecoveryEventHandler</c>. Routes ships that are out of fuel or
/// critically low on fuel to a known fuel market.
///
/// This planner matches every assignment (it is registered before role planners) so it
/// can preempt the role planner's decision. It returns <see cref="ShipPlannerCommandKind.None"/>
/// when no recovery action is required, allowing <see cref="ShipPlannerService"/> to fall
/// through to the next matching planner.
/// </summary>
public sealed class FuelRecoveryShipPlanner : IShipPlanner
{
    private const decimal CriticalFuelRatio = 0.15m;

    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment) => ship is not null && assignment is not null;

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return PlanDocked(ship, context);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Fuel recovery planner only handles in-orbit or docked decisions.");
        }

        if (ship.FuelCapacity <= 0)
        {
            if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
            {
                return ShipPlannerDecision.PatchFlightMode(ship.Symbol, "DRIFT", "Ship has no fuel tank; switching to DRIFT.");
            }

            return ShipPlannerDecision.None(ship.Symbol, "Ship has no fuel tank; nothing to refuel.");
        }

        var fuelRatio = (decimal)ship.FuelCurrent / ship.FuelCapacity;
        var isCriticallyLow = fuelRatio <= CriticalFuelRatio;
        if (ship.FuelCurrent > 0 && !isCriticallyLow)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Fuel level is acceptable.");
        }

        if (string.IsNullOrWhiteSpace(context.FuelMarketWaypoint))
        {
            return ShipPlannerDecision.None(ship.Symbol, "No known fuel market in current system.");
        }

        if (!string.Equals(ship.FlightMode, "DRIFT", StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.PatchFlightMode(ship.Symbol, "DRIFT", "Critically low fuel; switching to DRIFT before navigating to fuel market.");
        }

        if (string.Equals(ship.WaypointSymbol, context.FuelMarketWaypoint, StringComparison.OrdinalIgnoreCase))
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At fuel market with critically low fuel; docking to refuel.");
        }

        return ShipPlannerDecision.Navigate(ship.Symbol, context.FuelMarketWaypoint, "Coasting to nearest fuel market.");
    }

    private static ShipPlannerDecision PlanDocked(ShipModel ship, ShipPlannerContext context)
    {
        if (ship.FuelCapacity <= 0 || ship.FuelCurrent >= ship.FuelCapacity)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Docked ship does not need fuel.");
        }

        if (!context.CurrentWaypointSellsFuel)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Docked waypoint does not sell fuel.");
        }

        return ShipPlannerDecision.Refuel(ship.Symbol, "Docked at fuel market with empty tank; refueling.");
    }
}

using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Planning;

/// <summary>
/// Phase 4b: builder/construction-role ship planner migrated out of
/// <c>ShipInOrbitBuilderEventHandler</c> and the docked-builder branch of
/// <c>ShipDockedBuilderEventHandler</c>. The planner makes the next decision for ships
/// with the "Builder" assignment based on cargo, location, and construction progress.
///
/// Decision tree (mirrors the legacy chain handler):
///   1. Construction site complete -> AssignIdle.
///   2. Builder assignment missing destination/cargo metadata -> AssignIdle.
///   3. Docked: SupplyCompleted -> AssignIdle; cargo short -> BuyCargo; otherwise Orbit.
///   4. In orbit at construction site with cargo -> SupplyConstruction.
///   5. In orbit at construction site without cargo -> Navigate to origin.
///   6. In orbit with cargo elsewhere -> Navigate to construction site.
///   7. In orbit at origin without cargo -> Dock to load.
///   8. In orbit elsewhere without cargo -> Navigate to origin.
/// </summary>
public sealed class BuilderShipPlanner : IShipPlanner
{
    public bool CanPlan(ShipModel ship, ShipAssignmentDto assignment)
    {
        if (ship is null || assignment is null)
        {
            return false;
        }

        return assignment.AssignmentType.Equals("Builder", StringComparison.OrdinalIgnoreCase);
    }

    public ShipPlannerDecision Plan(ShipModel ship, ShipAssignmentDto assignment, ShipPlannerContext context)
    {
        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Ship is in transit; waiting for arrival.");
        }

        if (string.IsNullOrWhiteSpace(assignment.DestWaypoint) || string.IsNullOrWhiteSpace(assignment.CargoSymbol))
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Builder assignment is missing destination or cargo symbol.");
        }

        if (context.ConstructionComplete)
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Construction site is complete.");
        }

        var cargoSymbol = assignment.CargoSymbol!;
        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(c => c.Symbol.Equals(cargoSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            return PlanDocked(ship, assignment, cargoSymbol, cargoUnits);
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            return ShipPlannerDecision.None(ship.Symbol, "Builder planner only handles docked or in-orbit decisions.");
        }

        return PlanInOrbit(ship, assignment, cargoUnits);
    }

    private static ShipPlannerDecision PlanDocked(ShipModel ship, ShipAssignmentDto assignment, string cargoSymbol, int cargoUnits)
    {
        if (assignment.SupplyCompleted)
        {
            return ShipPlannerDecision.AssignIdle(ship.Symbol, "Supply run completed.");
        }

        var targetUnits = assignment.RequiredUnits > 0 ? assignment.RequiredUnits : 1;
        var cargoCapacity = ship.CargoCapacity > 0 ? ship.CargoCapacity : targetUnits;
        var unitsToBuy = Math.Min(targetUnits - cargoUnits, cargoCapacity - ship.CargoCurrent);

        if (unitsToBuy > 0)
        {
            return ShipPlannerDecision.BuyCargo(ship.Symbol, cargoSymbol, unitsToBuy, $"Buying {unitsToBuy}x {cargoSymbol} for construction.");
        }

        return ShipPlannerDecision.Orbit(ship.Symbol, "Builder loaded with materials; orbiting to depart for construction site.");
    }

    private static ShipPlannerDecision PlanInOrbit(ShipModel ship, ShipAssignmentDto assignment, int cargoUnits)
    {
        var currentWaypoint = ship.WaypointSymbol ?? string.Empty;
        var atConstructionSite = currentWaypoint.Equals(assignment.DestWaypoint, StringComparison.OrdinalIgnoreCase);
        var atOrigin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            && currentWaypoint.Equals(assignment.OriginWaypoint, StringComparison.OrdinalIgnoreCase);

        if (atConstructionSite && cargoUnits > 0)
        {
            var targetUnits = assignment.RequiredUnits > 0 ? assignment.RequiredUnits : cargoUnits;
            var units = Math.Min(cargoUnits, targetUnits);
            var system = ExtractSystemSymbol(assignment.DestWaypoint!);
            return ShipPlannerDecision.SupplyConstruction(
                ship.Symbol,
                system,
                assignment.DestWaypoint!,
                assignment.CargoSymbol!,
                units,
                $"Supplying {units}x {assignment.CargoSymbol} at construction site.");
        }

        if (atConstructionSite && cargoUnits == 0)
        {
            var origin = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
                ? assignment.OriginWaypoint!
                : currentWaypoint;
            return ShipPlannerDecision.Navigate(ship.Symbol, origin, "At construction site without cargo; navigating back to origin.");
        }

        if (cargoUnits > 0)
        {
            return ShipPlannerDecision.Navigate(ship.Symbol, assignment.DestWaypoint!, "Cargo loaded; navigating to construction site.");
        }

        if (atOrigin)
        {
            return ShipPlannerDecision.Dock(ship.Symbol, "At builder origin without cargo; docking to load.");
        }

        var reload = !string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
            ? assignment.OriginWaypoint!
            : currentWaypoint;
        return ShipPlannerDecision.Navigate(ship.Symbol, reload, "No cargo and not at origin; navigating to origin.");
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}

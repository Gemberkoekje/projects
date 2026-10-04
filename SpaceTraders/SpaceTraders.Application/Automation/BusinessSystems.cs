using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The systems the plans do business in (asked on 2026-10-04, while the command ship explores: "For now: come home. Long
/// term: plans should just work across systems … But let's start simple"): the systems where a ship of ours is, not counting
/// a ship that explores. What it finds there is information, for the systems dashboard: no plan buys a ship, or plans work,
/// in a system because the explorer is in it.
/// </summary>
public static class BusinessSystems
{
    /// <summary>The ships that explore: those with an open explore assignment.</summary>
    /// <param name="activeAssignments">The open assignments.</param>
    /// <returns>Their ships, by symbol.</returns>
    public static IReadOnlySet<string> Explorers(IEnumerable<ShipAssignmentDto> activeAssignments)
    {
        ArgumentNullException.ThrowIfNull(activeAssignments);
        return activeAssignments
            .Where(assignment => !assignment.CompletedAt.HasValue
                && assignment.AssignmentType.Equals(ExplorePlanService.AssignmentType, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.ShipSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The systems where a ship of ours is that doesn't explore, in symbol order.</summary>
    /// <param name="fleet">The fleet.</param>
    /// <param name="explorers">The ships that explore (<see cref="Explorers"/>).</param>
    /// <returns>The systems, by symbol.</returns>
    public static IReadOnlyList<string> Of(IEnumerable<ShipModel> fleet, IReadOnlySet<string> explorers)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        ArgumentNullException.ThrowIfNull(explorers);
        return [.. fleet
            .Where(ship => !explorers.Contains(ship.Symbol))
            .Select(ship => ship.SystemSymbol)
            .OfType<string>()
            .Where(system => system.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)];
    }
}

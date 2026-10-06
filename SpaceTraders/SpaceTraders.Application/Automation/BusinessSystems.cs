using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The systems the plans do business in (asked on 2026-10-04, while the command ship explores: "For now: come home. Long
/// term: plans should just work across systems … But let's start simple"): the headquarters' system alone, as D60 meant
/// (PLAN.md slice 6.28). What a ship finds elsewhere is information, for the systems dashboard: no plan buys a ship, or plans
/// work, in a system because the explorer, a probe that watches its markets, or a trader is in it. Only the trading plan's
/// traders cross systems (slice 6.29, D96), and the role board gives a ship abroad the trade role alone.
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

    /// <summary>The systems the plans do business in: the headquarters' system (D60, slice 6.28).</summary>
    /// <param name="agent">The agent, as cached; null before startup sync caches it.</param>
    /// <returns>The headquarters' system; none while the agent or its headquarters isn't known.</returns>
    public static IReadOnlyList<string> Of(AgentModel? agent)
        => agent?.HeadquartersSymbol is { Length: > 0 } headquarters ? [WaypointSymbols.SystemOf(headquarters)] : [];
}

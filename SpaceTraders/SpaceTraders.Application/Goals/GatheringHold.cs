using System.Collections.Concurrent;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Goals;

/// <summary>
/// Holds the gatherers back while the requests are busy (PLAN.md slice 6.41, D125), asked on 2026-10-11: "I'd rather have
/// miners wait but have up to date markets and exploring than working miners but needing to wait for the explore", because
/// "Mining only gives a tiny amount of the total profits". A gathering goal is one the mining, siphon or survey plan gives
/// (<see cref="IsGathering"/>); its step is held back while fewer than half the burst is left (<see cref="IsBusy"/>), and the
/// next tick tries again, the gatherers last.
/// </summary>
/// <remarks>A singleton; thread-safe. A restart forgets when a ship was last held back.</remarks>
public sealed class GatheringHold(IRequestPressure pressure)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _heldAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="goal"/> gathers: the mining, siphon or survey plan's (D125).</summary>
    /// <param name="goal">The goal; none gathers nothing.</param>
    /// <returns>True for a gathering goal.</returns>
    public static bool IsGathering(ShipGoal? goal)
        => goal is not null && AutomationSwitches.PlanFor(goal) is AutomationPlan.Mining or AutomationPlan.Siphon or AutomationPlan.Survey;

    /// <summary>Whether the requests are busy at <paramref name="now"/>: fewer than half the burst left.</summary>
    /// <param name="now">The time to judge by.</param>
    /// <returns>True while busy.</returns>
    public bool IsBusy(DateTimeOffset now) => pressure.BurstRemaining(now) * 2 < pressure.BurstSize;

    /// <summary>
    /// Holds the step of <paramref name="goal"/> back when it gathers and the requests are busy, and notes when.
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="goal">Its active goal.</param>
    /// <param name="now">The time.</param>
    /// <returns>True when the step is held back.</returns>
    public bool TryHold(string shipSymbol, ShipGoal goal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(shipSymbol);
        if (!IsGathering(goal) || !IsBusy(now))
        {
            return false;
        }

        _heldAt[shipSymbol] = now;
        return true;
    }

    /// <summary>When the ship's step was last held back, for <c>ShipStuck</c>: a held ship waits on purpose.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <returns>The last hold, or <see cref="DateTimeOffset.MinValue"/> for none.</returns>
    public DateTimeOffset LastHeldAt(string shipSymbol)
        => _heldAt.TryGetValue(shipSymbol, out var heldAt) ? heldAt : DateTimeOffset.MinValue;
}

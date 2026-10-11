using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Rules;

/// <summary>Who comes to the counter when a slot comes up (BUILD_BRIEF §5.13).</summary>
public static class SchedulingRules
{
    /// <summary>Resolves one of today's slots.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="day">Today.</param>
    /// <param name="slotIndex">The slot.</param>
    /// <returns>Who comes, if anyone.</returns>
    public static SlotResolution Resolve(ContentSet content, GameState state, int day, int slotIndex)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        var slotId = content.Day(day).Slots[slotIndex];
        if (slotId == ContentSet.FillerSlot)
        {
            return new SlotResolution(slotId, NextFiller(content, state, day), "", false);
        }

        var visitId = NextVisit(content, state, content.Patron(slotId), day);
        return visitId.Length > 0
            ? new SlotResolution(slotId, visitId, slotId, true)
            : new SlotResolution(slotId, NextFiller(content, state, day), slotId, false);
    }

    /// <summary>
    /// The visit a patron would make today: their lowest step with no used visit, and among that
    /// step's visits in file order, the first whose earliest day, spacing and condition hold.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="patron">The patron.</param>
    /// <param name="day">Today.</param>
    /// <returns>The visit id, or empty when no visit qualifies.</returns>
    public static string NextVisit(ContentSet content, GameState state, Patron patron, int day)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(patron);
        var met = state.HasMet(patron.Id);
        IReadOnlySet<int> used = met ? state.Patron(patron.Id).UsedSteps : new HashSet<int>();
        var open = patron.Visits.Select(v => v.Step).Where(s => !used.Contains(s)).Order().ToList();
        if (open.Count == 0)
        {
            return "";
        }

        var facts = ConditionRules.FactsFor(content, state, patron.Id);
        foreach (var visit in patron.Visits.Where(v => v.Step == open[0]))
        {
            if (visit.EarliestDay > day)
            {
                continue;
            }

            if (met && day - state.Patron(patron.Id).LastVisitDay < visit.MinDaysAfterPrevious)
            {
                continue;
            }

            if (ConditionRules.Holds(visit.When, facts))
            {
                return visit.Id;
            }
        }

        return "";
    }

    /// <summary>The next unused one-off visitor, by fillerOrder, whose visit qualifies today.</summary>
    /// <param name="content">The content.</param>
    /// <param name="state">The state.</param>
    /// <param name="day">Today.</param>
    /// <returns>The visit id, or empty when nobody is left.</returns>
    public static string NextFiller(ContentSet content, GameState state, int day)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        foreach (var filler in content.Fillers.Where(f => !state.HasMet(f.Id)))
        {
            var visitId = NextVisit(content, state, filler, day);
            if (visitId.Length > 0)
            {
                return visitId;
            }
        }

        return "";
    }

    /// <summary>
    /// Whether a patron has a slot by id still to come on or after a day: later today, or on a
    /// later day of the week.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="patronId">The patron.</param>
    /// <param name="today">Today.</param>
    /// <param name="currentSlot">The slot being played today.</param>
    /// <param name="fromDay">The earliest day that counts.</param>
    /// <returns>True when such a slot exists.</returns>
    public static bool HasSlotAhead(ContentSet content, string patronId, int today, int currentSlot, int fromDay)
    {
        ArgumentNullException.ThrowIfNull(content);
        for (var day = Math.Max(today, fromDay); day <= content.WeekDays; day++)
        {
            var slots = content.Day(day).Slots;
            for (var i = day == today ? currentSlot + 1 : 0; i < slots.Count; i++)
            {
                if (slots[i] == patronId)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

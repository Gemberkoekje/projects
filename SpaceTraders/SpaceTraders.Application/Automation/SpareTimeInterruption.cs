using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Takes a ship off its spare-time trip for more important work (PLAN.md slice 6.8): a survey, which keeps the hold
/// aboard (D37), or a trade, which sells it first (D34). Only while the trip still fills its hold: a trip that sells is
/// nearly done, and the plans choose again after it. And only when replacing the trip's goal is safe:
/// <list type="bullet">
///   <item>no goal step of the ship runs (<see cref="IShipGoalStepGuard"/>, B46): a step that turns the trip to selling
///   would write the trip back over the new goal;</item>
///   <item>the ship as stored isn't in flight: its arrival matches the goal that flew it (B17), and with another goal
///   nothing would record it.</item>
/// </list>
/// </summary>
public sealed class SpareTimeInterruption(
    IShipRepository ships,
    IShipGoalRepository goals,
    IShipGoalStepGuard stepGuard,
    ILogger<SpareTimeInterruption> logger)
{
    /// <summary>Whether a ship is on a spare-time trip that more important work may take it off: it fills its hold, and isn't in flight.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="goal">Its active goal, if any.</param>
    /// <param name="hasOpenAssignment">Whether it has an open assignment.</param>
    /// <returns>True for a ship a survey or a trade may take.</returns>
    public static bool IsInterruptible(ShipModel ship, ShipGoal? goal, bool hasOpenAssignment)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return goal is GatherAndSellGoal { Selling: false, Status: not GoalStatus.Blocked and not GoalStatus.Completed }
            && !hasOpenAssignment
            && ship.LocalStatus != ShipLocalStatus.InTransit;
    }

    /// <summary>
    /// Replaces the ship's spare-time trip with <paramref name="next"/>, when the trip still fills its hold and that is
    /// safe; otherwise leaves it, and a later tick tries again.
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="next">The goal it gets instead.</param>
    /// <param name="reason">What takes it, for the journal: <c>survey</c> or <c>trade</c>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>True when the ship has the new goal.</returns>
    public async Task<bool> TryReplaceAsync(string shipSymbol, ShipGoal next, string reason, CancellationToken cancellationToken)
    {
        if (!stepGuard.TryEnter(shipSymbol))
        {
            logger.LogDebug("Spare time: a goal step of ship {ShipSymbol} runs; its trip is interrupted on a later tick.", shipSymbol);
            return false;
        }

        try
        {
            // Read again under the guard, the ship as stored: not dead-reckoned, so a flight whose arrival hasn't been
            // recorded still counts as one.
            var trip = await goals.GetActiveGoalAsync(shipSymbol, cancellationToken) as GatherAndSellGoal;
            var ship = await ships.FindAsync(shipSymbol, cancellationToken);
            if (trip is null || ship is null || !IsInterruptible(ship, trip, hasOpenAssignment: false))
            {
                logger.LogDebug("Spare time: ship {ShipSymbol} is no longer on a trip that fills its hold, or is in flight; it isn't interrupted.", shipSymbol);
                return false;
            }

            await goals.SetActiveGoalAsync(shipSymbol, next, cancellationToken);
            logger.LogInformation(
                "{EventKind:l}: ship {ShipSymbol} stops gathering at {WaypointSymbol} for a {Reason}, with {Units} units aboard.",
                JournalEvents.GatheringInterrupted,
                shipSymbol,
                trip.SourceWaypointSymbol,
                reason,
                ship.CargoCurrent);
            return true;
        }
        finally
        {
            stepGuard.Exit(shipSymbol);
        }
    }
}

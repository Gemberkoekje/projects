using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Trading;

/// <summary>
/// The credits trade trips hold back for their cargo (D57): from the moment a trip starts towards its buy market until its
/// cargo is aboard, its <see cref="TradeBetweenMarketsGoal.ReservedCredits"/>. The trading plan gives other traders only
/// the credits no trip holds back, a trip at its buy market spends its own and those no other trip holds, and the credits
/// every ship purchase must leave grow by them (<see cref="Orchestration.BudgetPolicy"/>). Asked on 2026-10-03: "Let's have
/// these credits reserved as soon as a ship starts towards it, so that this cannot happen (waste of time and fuel)".
/// </summary>
/// <remarks>
/// Kept with the trip's goal, so a restart keeps it. A trip that has bought, or is blocked or done, holds back nothing.
/// </remarks>
public static class TripReservations
{
    /// <summary>What one trip holds back now.</summary>
    /// <param name="trip">The trip.</param>
    /// <returns>Its reserved credits until its cargo is aboard, else 0.</returns>
    public static long HeldBack(TradeBetweenMarketsGoal trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return !trip.CargoBought && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed
            ? Math.Max(0, trip.ReservedCredits)
            : 0;
    }

    /// <summary>What the fleet's trips hold back now, but one ship's.</summary>
    /// <param name="trips">The trips, by ship symbol.</param>
    /// <param name="exceptShipSymbol">The ship whose trip doesn't count: the credits it holds back are its own to spend.</param>
    /// <returns>The credits held back.</returns>
    public static long HeldBack(IEnumerable<KeyValuePair<string, TradeBetweenMarketsGoal>> trips, string exceptShipSymbol = "")
    {
        ArgumentNullException.ThrowIfNull(trips);
        return trips
            .Where(trip => !trip.Key.Equals(exceptShipSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(trip => HeldBack(trip.Value));
    }
}

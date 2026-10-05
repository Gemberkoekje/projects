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
/// Kept with the trip's goal, so a restart keeps it. A trip that has bought, or is blocked or done, holds back nothing. A
/// construction trip holds back its cargo the same way (slice 6.6, D64): traders, other trips and ship purchases leave it.
/// </remarks>
public static class TripReservations
{
    /// <summary>
    /// Whether a trip is on its way to buy: started, its cargo not yet aboard, and neither blocked nor done. Until then it holds
    /// back its credits (D57), and its good at its buy market (D77, <see cref="HeldBuys"/>).
    /// </summary>
    /// <param name="trip">The trip.</param>
    /// <returns>True while it has yet to buy.</returns>
    public static bool IsOnItsWayToBuy(TradeBetweenMarketsGoal trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return !trip.CargoBought && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed;
    }

    /// <summary>Whether a construction trip is on its way to buy, as a trade trip can be (slice 6.6, D64).</summary>
    /// <param name="trip">The trip.</param>
    /// <returns>True while it has yet to buy.</returns>
    public static bool IsOnItsWayToBuy(SupplyConstructionGoal trip)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return !trip.CargoBought && trip.Status is not GoalStatus.Blocked and not GoalStatus.Completed;
    }

    /// <summary>What one trip holds back now.</summary>
    /// <param name="trip">The trip.</param>
    /// <returns>Its reserved credits until its cargo is aboard, else 0.</returns>
    public static long HeldBack(TradeBetweenMarketsGoal trip)
        => IsOnItsWayToBuy(trip) ? Math.Max(0, trip.ReservedCredits) : 0;

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

    /// <summary>What one construction trip holds back now (slice 6.6, D64): what its cargo costs, until it is aboard.</summary>
    /// <param name="trip">The trip.</param>
    /// <returns>Its reserved credits until its cargo is aboard, else 0.</returns>
    public static long HeldBack(SupplyConstructionGoal trip)
        => IsOnItsWayToBuy(trip) ? Math.Max(0, trip.ReservedCredits) : 0;

    /// <summary>What the fleet's construction trips hold back now, but one ship's.</summary>
    /// <param name="trips">The trips, by ship symbol.</param>
    /// <param name="exceptShipSymbol">The ship whose trip doesn't count: the credits it holds back are its own to spend.</param>
    /// <returns>The credits held back.</returns>
    public static long HeldBack(IEnumerable<KeyValuePair<string, SupplyConstructionGoal>> trips, string exceptShipSymbol = "")
    {
        ArgumentNullException.ThrowIfNull(trips);
        return trips
            .Where(trip => !trip.Key.Equals(exceptShipSymbol, StringComparison.OrdinalIgnoreCase))
            .Sum(trip => HeldBack(trip.Value));
    }
}

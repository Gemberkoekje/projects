using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Ports;

/// <summary>
/// Which requests to the API go first while requests wait for the rate limit (PLAN.md slice 6.33, D115), asked on 2026-10-07:
/// "I'd like trade ships to be prioritized in rate limiting. So if a trade ship docks/undocks/jumps/navigates/buys/sells it
/// should not have to wait for a miner or a surveyor." A trade trip's requests are marked where they start: its goal steps
/// (<c>ShipGoalExecutorService</c>), and its flights' departures and arrivals (<c>NavigateToWaypointHandler</c>,
/// <c>NavigateToWaypointArrivedHandler</c>), which the arrival's schedule runs apart from the steps. The mark flows with the
/// calls made from there (<see cref="AsyncLocal{T}"/>) into the handler that keeps the rate limit, <c>RateLimitingHandler</c>,
/// which sends them before every other request.
/// </summary>
public static class ApiPriority
{
    private static readonly AsyncLocal<bool> TradeTrip = new();

    /// <summary>Whether the requests made now, in this flow, are a trade trip's.</summary>
    public static bool IsTradeTrip => TradeTrip.Value;

    /// <summary>
    /// Marks the requests made from here on, in this flow, as a trade trip's while <paramref name="goal"/> is one
    /// (<see cref="TradeBetweenMarketsGoal"/>), until the scope is disposed; any other goal, or none, marks nothing.
    /// </summary>
    /// <param name="goal">The goal the requests serve.</param>
    /// <returns>The scope; disposing it puts back what was marked before.</returns>
    public static IDisposable For(ShipGoal? goal)
    {
        var before = TradeTrip.Value;
        TradeTrip.Value = before || goal is TradeBetweenMarketsGoal;
        return new Scope(before);
    }

    private sealed class Scope(bool before) : IDisposable
    {
        public void Dispose() => TradeTrip.Value = before;
    }
}

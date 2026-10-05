namespace SpaceTraders.Application.Trading;

/// <summary>
/// The routes a new cargo ship would have waited for (PLAN.md slice 6.21, D88): each pass of the trading plan notes the routes
/// worth at least <c>Trade.ShipPurchaseMinRouteProfit</c> that a new ship would have from the shipyard and no trader holds, and
/// how long each has waited without a break. Beyond <c>Trade.ShipPurchases</c> a cargo ship is bought only once one has waited
/// <c>Trade.ShipPurchaseWaitMinutes</c> with every trader busy: the traders can't keep up, so another one adds value. Asked on
/// 2026-10-05: "can you add a limitation on buying more trade ships unless a trade ship actually adds value? If the market is
/// stable, we have too many trade ships right now." In memory: a start waits anew.
/// </summary>
/// <remarks>Thread-safe.</remarks>
public sealed class TradeShipDemand
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _waitingSince = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Notes the routes waiting now: one seen for the first time waits from now, and one no longer among them is forgotten.
    /// </summary>
    /// <param name="routeKeys">The routes waiting now (<see cref="TradeRoutePlanner.RouteKey"/>).</param>
    /// <param name="now">The time.</param>
    /// <returns>How long the route that has waited longest has waited; zero without one.</returns>
    public TimeSpan Note(IEnumerable<string> routeKeys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(routeKeys);

        var waiting = routeKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var gone in _waitingSince.Keys.Where(key => !waiting.Contains(key)).ToList())
            {
                _waitingSince.Remove(gone);
            }

            foreach (var key in waiting)
            {
                _waitingSince.TryAdd(key, now);
            }

            return _waitingSince.Count == 0 ? TimeSpan.Zero : now - _waitingSince.Values.Min();
        }
    }

    /// <summary>As if every route waiting now had waited this much longer: for the tests of what waiting long enough does.</summary>
    /// <param name="by">How much longer.</param>
    internal void Backdate(TimeSpan by)
    {
        lock (_gate)
        {
            foreach (var key in _waitingSince.Keys.ToList())
            {
                _waitingSince[key] -= by;
            }
        }
    }
}

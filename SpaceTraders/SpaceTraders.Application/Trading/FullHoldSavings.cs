namespace SpaceTraders.Application.Trading;

/// <summary>
/// The full holds traders save up for (D56): a trader whose best route, credits aside, is a full hold the credits don't pay
/// for yet takes other work meanwhile, and the credits every ship purchase must leave grow by the dearest of these holds
/// (<see cref="Orchestration.BudgetPolicy"/>), so ships are bought after it. Asked on 2026-10-03: "Full hold or nothing, when
/// this occurs the credit floor should be temporarily expanded so any ship purchases wait for the full hold to be bought
/// before new ships are bought." The trading plan notes a saving when the trader is free, keeps it while the trader works,
/// and the trade executor ends it when that hold is bought.
/// </summary>
/// <remarks>
/// In memory, a singleton: a restart forgets the savings, and the trading plan notes them again at its first pass.
/// Thread-safe: the plans and the executors run on the same tick, an arrival at any time.
/// </remarks>
public sealed class FullHoldSavings
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FullHoldSaving> _savings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Notes that a trader saves up for a route's full hold.</summary>
    /// <param name="shipSymbol">The trader.</param>
    /// <param name="routeKey">The route (<see cref="TradeRoutePlanner.RouteKey"/>).</param>
    /// <param name="credits">What the full hold and the trip's fuel cost.</param>
    /// <returns>True when that is new: another route, or another sum, than the trader saved up for before.</returns>
    public bool SaveFor(string shipSymbol, string routeKey, long credits)
    {
        ArgumentNullException.ThrowIfNull(shipSymbol);
        ArgumentNullException.ThrowIfNull(routeKey);

        var saving = new FullHoldSaving(shipSymbol, routeKey, credits);
        lock (_gate)
        {
            if (_savings.TryGetValue(shipSymbol, out var known) && known == saving)
            {
                return false;
            }

            _savings[shipSymbol] = saving;
            return true;
        }
    }

    /// <summary>What a trader saves up for, when it does.</summary>
    /// <param name="shipSymbol">The trader.</param>
    /// <param name="saving">Its saving.</param>
    /// <returns>True when it saves up for a full hold.</returns>
    public bool TryGet(string shipSymbol, out FullHoldSaving saving)
    {
        ArgumentNullException.ThrowIfNull(shipSymbol);
        lock (_gate)
        {
            if (_savings.TryGetValue(shipSymbol, out var known))
            {
                saving = known;
                return true;
            }
        }

        saving = new FullHoldSaving(shipSymbol, string.Empty, 0);
        return false;
    }

    /// <summary>Forgets what a trader saved up for: it bought that hold, or its best route is one it can pay for.</summary>
    /// <param name="shipSymbol">The trader.</param>
    public void Clear(string shipSymbol)
    {
        ArgumentNullException.ThrowIfNull(shipSymbol);
        lock (_gate)
        {
            _savings.Remove(shipSymbol);
        }
    }

    /// <summary>Forgets the savings of ships that no longer trade.</summary>
    /// <param name="traderSymbols">The ships that trade now.</param>
    public void KeepOnly(IReadOnlySet<string> traderSymbols)
    {
        ArgumentNullException.ThrowIfNull(traderSymbols);
        lock (_gate)
        {
            foreach (var ship in _savings.Keys.Where(ship => !traderSymbols.Contains(ship)).ToList())
            {
                _savings.Remove(ship);
            }
        }
    }

    /// <summary>The dearest full hold a trader saves up for: what the credit floor grows by. 0 without one.</summary>
    /// <returns>The credits.</returns>
    public long Largest()
    {
        lock (_gate)
        {
            return _savings.Values.Select(saving => saving.Credits).DefaultIfEmpty(0).Max();
        }
    }
}

/// <summary>A full hold a trader saves up for (D56): the route, and what the hold and the trip's fuel cost.</summary>
public sealed record FullHoldSaving
{
    /// <summary>Creates a saving.</summary>
    /// <param name="ShipSymbol">The trader.</param>
    /// <param name="RouteKey">The route (<see cref="TradeRoutePlanner.RouteKey"/>).</param>
    /// <param name="Credits">What the full hold and the trip's fuel cost.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public FullHoldSaving(string ShipSymbol, string RouteKey, long Credits)
    {
        this.ShipSymbol = ShipSymbol;
        this.RouteKey = RouteKey;
        this.Credits = Credits;
    }

    /// <summary>The trader.</summary>
    public required string ShipSymbol { get; init; }

    /// <summary>The route (<see cref="TradeRoutePlanner.RouteKey"/>).</summary>
    public required string RouteKey { get; init; }

    /// <summary>What the full hold and the trip's fuel cost.</summary>
    public required long Credits { get; init; }
}

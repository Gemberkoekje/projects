using System.Collections.Concurrent;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// What each gathering plan (mining, siphon, construction) left free at its last pass (B63): the ships it works with, and
/// the free ones among them it had no work for. Those plans run before the trading plan in every tick, and a ship of theirs
/// trades only when its own plan had nothing for it (D58). An arrival's goal step runs outside the tick, so a trip can end
/// after its plan's pass and before the trading plan's: that ship wasn't passed over, and waits for its plan's next pass,
/// one tick later.
/// </summary>
/// <remarks>A singleton, in memory: a restart starts empty, and each plan records again at its first pass.</remarks>
public sealed class PassedOverShips
{
    private readonly ConcurrentDictionary<AutomationPlan, Pass> _passes = new();

    /// <summary>Records a plan's pass: the ships it works with, and the free ones among them it had no work for.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="own">The ships the plan works with, free or not.</param>
    /// <param name="passedOver">The free ones it gave no work.</param>
    public void Record(AutomationPlan plan, IEnumerable<string> own, IEnumerable<string> passedOver)
        => _passes[plan] = new Pass(
            own.ToHashSet(StringComparer.OrdinalIgnoreCase),
            passedOver.ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the trading plan may give the ship a route, as far as the gathering plans are concerned: none of
    /// <paramref name="plansOn"/> worked with it at its last pass, or the one that did passed it over.
    /// </summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="plansOn">The gathering plans that are switched on; a plan that is off has no say.</param>
    /// <returns>True when the ship may trade.</returns>
    public bool MayTrade(string shipSymbol, IReadOnlyCollection<AutomationPlan> plansOn)
    {
        ArgumentNullException.ThrowIfNull(plansOn);
        return plansOn.All(plan => !_passes.TryGetValue(plan, out var pass)
            || !pass.Own.Contains(shipSymbol)
            || pass.PassedOver.Contains(shipSymbol));
    }

    private sealed record Pass(IReadOnlySet<string> Own, IReadOnlySet<string> PassedOver);
}

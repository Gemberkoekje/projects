namespace SpaceTraders.Application.Construction;

/// <summary>
/// What stops the home jump gate from being built, as the construction plan saw it on its last pass (PLAN.md slice 6.51,
/// D139). Asked on 2026-11-08: "I want to know what stops the gate from being built. Is it a lack of credits, is it
/// scarce/limited prereqs, is it not enough ships available to do it? Should show over time, so if at one point ships is
/// the problem and at another point credits, I want to be able to see that." One reason a pass, the first that holds, in
/// this order:
/// <list type="number">
///   <item><see cref="Done"/>: the gate needs no materials;</item>
///   <item><see cref="NoMarket"/>: no market a builder can reach sells what the gate still needs;</item>
///   <item><see cref="LowSupply"/> or <see cref="MarketBusy"/>: every market that sells it has it SCARCE or LIMITED (D66),
///   or has another of our trips on its way to buy it (D80);</item>
///   <item><see cref="PurchaseOrder"/>: a purchase comes before the gate's loads in the order ships are bought in (D64,
///   D136): the gate can't be built yet;</item>
///   <item><see cref="Credits"/>: the next load costs more than a load may spend (D137);</item>
///   <item><see cref="Ships"/>: a load is ready, but no builder is free with an empty hold to take it, or none is in the
///   gate's system;</item>
///   <item><see cref="Building"/>: nothing waits: every unit the gate still needs is on its way.</item>
/// </list>
/// <see cref="Off"/> is the metrics' own: the construction plan or the automation is switched off, so no pass says.
/// </summary>
public static class ConstructionBlockers
{
    /// <summary>The gate needs no materials.</summary>
    public const string Done = "done";

    /// <summary>Every unit the gate still needs is on its way: nothing waits.</summary>
    public const string Building = "building";

    /// <summary>A load is ready, but no builder is free with an empty hold to take it.</summary>
    public const string Ships = "ships";

    /// <summary>The next load costs more than a load may spend (D64, D137).</summary>
    public const string Credits = "credits";

    /// <summary>Every market that sells what the gate needs has it SCARCE or LIMITED (D66).</summary>
    public const string LowSupply = ConstructionPlanner.LowSupply;

    /// <summary>Every market that would sell a load has another of our trips on its way to buy it there (D80).</summary>
    public const string MarketBusy = ConstructionPlanner.MarketBusy;

    /// <summary>A purchase comes before the gate's loads in the order ships are bought in (D64, D136).</summary>
    public const string PurchaseOrder = "purchase_order";

    /// <summary>No market a builder can reach sells what the gate still needs.</summary>
    public const string NoMarket = ConstructionPlanner.NoMarket;

    /// <summary>The construction plan or the automation is switched off.</summary>
    public const string Off = "off";

    /// <summary>The reasons, each at its code (<see cref="CodeOf"/>): what the dashboard maps the gauge's value back to.</summary>
    public static IReadOnlyList<string> All { get; } = [Done, Building, Ships, Credits, LowSupply, MarketBusy, PurchaseOrder, NoMarket, Off];

    /// <summary>A reason as the number the gauge carries (<c>spacetraders_construction_blocker</c>): its place in <see cref="All"/>.</summary>
    /// <param name="blocker">The reason.</param>
    /// <returns>0 for <see cref="Done"/> to 8 for <see cref="Off"/>; -1 for an unknown or empty reason.</returns>
    public static int CodeOf(string blocker)
    {
        ArgumentNullException.ThrowIfNull(blocker);
        for (var code = 0; code < All.Count; code++)
        {
            if (All[code].Equals(blocker, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return -1;
    }
}

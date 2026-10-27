namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// What a contract is offered on (design §3.6, §16.5): its price, its deadline, its term package, and its late fee as a
/// multiple of the package's (the cap, which is also the bond, stays the package's). A post is its poster's first offer; a
/// counter-offer may change any of the four. The rest of the terms follow from the price and the package
/// (<see cref="Simulation.Terms"/>), so an agreed offer re-scales them.
/// </summary>
public sealed record Offer(Credits Price, long Deadline, TermPackage Package, decimal LateFeeScale = 1m)
{
    /// <summary>The late-fee scales a counter may name: half the package's late fee, the package's, or double.</summary>
    public static readonly decimal[] LateFeeScales = { 0.5m, 1m, 2m };

    /// <summary>The offer a contract stands on now: as posted while open, as agreed once signed.</summary>
    public static Offer Of(Contract contract) => new(contract.Price, contract.DeadlineTick, contract.Terms.Package, contract.Terms.LateFeeScale);

    /// <summary>Whether it differs from <paramref name="other"/> in anything but the price.</summary>
    public bool SameTermsAs(Offer other) => Deadline == other.Deadline && Package == other.Package && LateFeeScale == other.LateFeeScale;
}

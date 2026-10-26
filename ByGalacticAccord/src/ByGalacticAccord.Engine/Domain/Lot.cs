namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// Goods one actor owns, in a ship's hold or in storage (design §16.1). Quality is as of <see cref="Since"/>;
/// a perishable keeps decaying from then, at the rate of the hold or store it sits in, until the next time
/// the lot is read for a handover, a load or an incident.
/// </summary>
public sealed record Lot(ActorId Owner, Cargo Cargo, long Since)
{
    public GoodType Good => Cargo.Good;

    public decimal Quantity => Cargo.Quantity;

    /// <summary>The lot as it stands at <paramref name="tick"/>, decayed at <paramref name="preservation"/> (0 standard, 0.8 refrigerated).</summary>
    public Lot At(long tick, decimal preservation) =>
        tick <= Since ? this : this with { Cargo = Cargo.Decayed(tick - Since, preservation), Since = tick };
}

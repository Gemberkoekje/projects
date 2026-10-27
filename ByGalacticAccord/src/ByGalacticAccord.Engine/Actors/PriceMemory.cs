using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>The kinds of post an AI actor prices from experience.</summary>
internal enum PriceKind
{
    /// <summary>A consumer's wanted Sale: the share of the spread it offers on top of the goods.</summary>
    Wanted,

    /// <summary>A producer's lot for sale: its price as a share of the ask.</summary>
    Lot,

    /// <summary>A shipper's haul: its offer as a multiple of the usual fee.</summary>
    Haul,
}

internal readonly record struct PriceKey(PriceKind Kind, GoodType Good);

/// <summary>
/// Price discovery from one's own posts (design §5.2: a price is what contracts are being signed and asked at). Before
/// each post the actor looks at how its last one of the kind went: haggled to another price, it moves toward the price
/// agreed; else taken within a day, it offers a little less generously; never taken, more.
/// </summary>
internal sealed class PriceMemory
{
    // Its posts priced this way that were still open when last looked at, oldest first.
    private readonly List<ContractId> _pending = new();

    public PriceMemory(decimal factor) => Factor = factor;

    /// <summary>How generous the next post is, on the kind's own scale.</summary>
    public decimal Factor { get; private set; }

    /// <summary>
    /// Learn from each of its posts that has closed since it last looked, oldest first: one signed at a haggled price moves
    /// the factor by the ratio of the price agreed to the price posted, weighted by <paramref name="haggleWeight"/> (how much
    /// the factor moves with the price); one taken within a day as posted multiplies it by <paramref name="onQuickTake"/>, one
    /// that expired by <paramref name="onExpiry"/>; then clamp the factor to [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    public decimal Next(SimulationContext context, decimal onQuickTake, decimal onExpiry, decimal min, decimal max, decimal haggleWeight = 1m)
    {
        _pending.RemoveAll(id =>
        {
            Contract post = context.Contract(id);
            if (post.IsOpen)
            {
                return false;
            }

            if (post.Status == ContractStatus.Expired)
            {
                Factor *= onExpiry;
            }
            else if (post.SignedTick is not null && post.Price != post.OfferedPrice && post.OfferedPrice.Amount > 0m)
            {
                Factor *= 1m + (((post.Price.Amount / post.OfferedPrice.Amount) - 1m) * haggleWeight);
            }
            else if (post.SignedTick is { } signed && signed - post.PostedTick <= 24)
            {
                Factor *= onQuickTake;
            }

            return true;
        });

        Factor = System.Math.Clamp(Factor, min, max);
        return Factor;
    }

    public void Posted(ContractId id) => _pending.Add(id);
}

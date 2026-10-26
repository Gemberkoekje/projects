using System;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// A parcel of goods. Quality runs 0..1 and decays for perishables outside a facility's own stock (design
/// §15.3); it is read by <c>QualityWarranty</c> clauses (§4.1.5) at handover.
/// </summary>
public readonly record struct Cargo(GoodType Good, decimal Quantity, decimal Quality)
{
    public static Cargo Fresh(GoodType good, decimal quantity) => new(good, quantity, 1.0m);

    /// <summary>Apply perishable decay over a number of ticks. <paramref name="preservation"/> 0..1 slows decay (refrigeration).</summary>
    public Cargo Decayed(long ticks, decimal preservation)
    {
        GoodInfo info = Goods.Info(Good);
        if (!info.Perishable || ticks <= 0)
        {
            return this;
        }

        decimal effectiveRate = info.QualityDecayPerTick * (1m - Math.Clamp(preservation, 0m, 1m));
        decimal newQuality = Math.Max(0m, Quality - (effectiveRate * ticks));
        return this with { Quality = newQuality };
    }

    /// <summary>Reduce quantity (e.g. partial cargo loss in a transit incident). Never below zero.</summary>
    public Cargo LosingUnits(decimal units) => this with { Quantity = Math.Max(0m, Quantity - units) };

    /// <summary>Reduce quality (e.g. cargo damage in a transit incident).</summary>
    public Cargo Damaged(decimal qualityLoss) => this with { Quality = Math.Max(0m, Quality - qualityLoss) };

    /// <summary>Two parcels of the same good as one: the quantities add and the quality is their weighted mean.</summary>
    public Cargo Merged(Cargo other)
    {
        if (other.Good != Good)
        {
            throw new ArgumentException("Only parcels of the same good merge.", nameof(other));
        }

        decimal quantity = Quantity + other.Quantity;
        decimal quality = quantity <= 0m ? Math.Max(Quality, other.Quality) : ((Quality * Quantity) + (other.Quality * other.Quantity)) / quantity;
        return new Cargo(Good, quantity, quality);
    }
}

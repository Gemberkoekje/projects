using System;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The three term packages (design §3.4) and the terms they give a Sale or a Haul. Every figure is scaled from the
/// posted price (a Haul's liability from the declared value of its goods), never from the poster's hidden reservation,
/// so the terms give nothing away (design §3.6). First-pass numbers, tuned with the soak harness:
/// <code>
///                       Sale: strict  standard  lenient     Haul: strict  standard  lenient
///   grace                       6 h      24 h      72 h             6 h      24 h      72 h
///   late fee per hour            1%      0.5%     0.25%              2%        1%      0.5%
///   late-fee cap (the bond)     25%       15%        8%             50%       30%       15%
///   break fee                   20%       10%        5%             30%       20%       10%
///   a short delivery is     failure  pro rata  pro rata         failure  pro rata  pro rata
///   warranty minimum            80%       70%       60%             80%       70%       60%
///     most refunded            100%       50%       30%            100%       50%       30%
///     may reject below          60%       45%         —             60%       45%         —
///   cargo liability cap           —         —         —            100%       50%         —
/// </code>
/// The warranty applies to perishables. A lot of goods already past fresh is warranted as seen: no more than the
/// quality they will still have at its deadline. A haggle may move the package and scale the late fee per hour
/// (<see cref="For(Contract, Offer)"/>); the figures then follow the agreed price.
/// </summary>
public static class Terms
{
    private static readonly Package[] SalePackages =
    {
        new(6, 0.01m, 0.25m, 0.2m, PartialDelivery.Failure, 0.8m, 1m, 0.6m, 0m),
        new(24, 0.005m, 0.15m, 0.1m, PartialDelivery.ProRata, Goods.StandardWarranty, 0.5m, 0.45m, 0m),
        new(72, 0.0025m, 0.08m, 0.05m, PartialDelivery.ProRata, 0.6m, 0.3m, null, 0m),
    };

    private static readonly Package[] HaulPackages =
    {
        new(6, 0.02m, 0.5m, 0.3m, PartialDelivery.Failure, 0.8m, 1m, 0.6m, 1m),
        new(24, 0.01m, 0.3m, 0.2m, PartialDelivery.ProRata, Goods.StandardWarranty, 0.5m, 0.45m, 0.5m),
        new(72, 0.005m, 0.15m, 0.1m, PartialDelivery.ProRata, 0.6m, 0.3m, null, 0m),
    };

    /// <summary>
    /// The terms of a Sale at <paramref name="price"/>. <paramref name="stored"/> is the quality now of a lot that waits in
    /// storage, where it keeps decaying, and <paramref name="hours"/> how long it is on offer. <paramref name="lateFeeScale"/>
    /// scales the late fee per hour (a haggled term); its cap, the bond, stays the package's.
    /// </summary>
    public static ContractTerms ForSale(TermPackage package, GoodType good, Credits price, decimal? stored = null, long hours = 0, decimal lateFeeScale = 1m)
    {
        Package p = SalePackages[(int)package];
        QualityWarranty? warranty = null;
        if (Goods.Info(good).Perishable)
        {
            // Sold as seen: a lot in storage is warranted only to the quality it will still have at its deadline.
            decimal seen = stored is { } q ? new Cargo(good, 1m, q).Decayed(hours, 0m).Quality : 1m;
            decimal min = Math.Min(p.MinQuality, Math.Round(seen, 2));
            decimal? floor = p.RejectBelow is { } f ? Math.Round(f * min / p.MinQuality, 2) : null;
            warranty = new QualityWarranty(min, p.MaxRefund, floor);
        }

        return new ContractTerms(
            package, p.GraceHours, LateFee(price, p, lateFeeScale), Credits.Of(Math.Round(price.Amount * p.BreakFee, 2)), p.Partial, warranty, null,
            lateFeeScale);
    }

    /// <summary>The terms of a Haul at <paramref name="fee"/>, of goods declared at <paramref name="unitValue"/> a unit.</summary>
    public static ContractTerms ForHaul(TermPackage package, GoodType good, Credits fee, decimal quantity, Credits unitValue, decimal lateFeeScale = 1m)
    {
        Package p = HaulPackages[(int)package];
        QualityWarranty? warranty = Goods.Info(good).Perishable ? new QualityWarranty(p.MinQuality, p.MaxRefund, p.RejectBelow) : null;
        CargoLiability? liability = p.LiabilityCap > 0m
            ? new CargoLiability(Credits.Of(Math.Round(unitValue.Amount, 2)), Credits.Of(Math.Round(unitValue.Amount * quantity * p.LiabilityCap, 2)))
            : null;
        return new ContractTerms(
            package, p.GraceHours, LateFee(fee, p, lateFeeScale), Credits.Of(Math.Round(fee.Amount * p.BreakFee, 2)), p.Partial, warranty, liability,
            lateFeeScale);
    }

    /// <summary>
    /// The terms <paramref name="contract"/> would have on <paramref name="offer"/>: its kind's terms rebuilt from the offer's
    /// price, package and late fee, so an agreed counter re-scales every figure (design §16.5). A lot in storage stays
    /// warranted as seen, to its new deadline.
    /// </summary>
    public static ContractTerms For(Contract contract, Offer offer) => contract switch
    {
        SaleContract sale => ForSale(offer.Package, sale.Good, offer.Price, sale.SeenQuality, offer.Deadline - sale.PostedTick, offer.LateFeeScale),
        HaulContract haul => ForHaul(offer.Package, haul.Good, offer.Price, haul.Quantity, haul.DeclaredUnitValue, offer.LateFeeScale),
        _ => contract.Terms,
    };

    /// <summary>
    /// The package a buyer of perishables posts when goods carried in a standard hold would arrive at
    /// <paramref name="arrival"/>: terms no carrier can meet only deter sellers (design §4.3), so it relaxes to the
    /// strictest package whose warranty a standard hold still meets. Goods only a refrigerated hold keeps (medicine on a
    /// long run) keep a floor that turns spoiled ones away.
    /// </summary>
    public static TermPackage Meetable(TermPackage package, GoodType good, decimal arrival)
    {
        if (!Goods.Info(good).Perishable)
        {
            return package;
        }

        if (arrival < SalePackages[(int)TermPackage.Lenient].MinQuality)
        {
            return package == TermPackage.Lenient ? TermPackage.Standard : package;
        }

        while (package != TermPackage.Lenient && arrival < SalePackages[(int)package].MinQuality)
        {
            package++;
        }

        return package;
    }

    /// <summary>
    /// The package an actor posts with, by personality (design §4.2): a quality-minded, risk-averse actor insists on strict
    /// terms, a careless or risk-hungry one is lenient. The player's default is standard; its composer chooses.
    /// </summary>
    public static TermPackage PackageFor(Actors.ActorState actor)
    {
        if (actor.IsPlayer)
        {
            return TermPackage.Standard;
        }

        decimal strictness = (actor.Personality.QualityBias + 1m - actor.Personality.RiskTolerance) / 2m;
        return strictness >= 0.6m ? TermPackage.Strict : strictness <= 0.4m ? TermPackage.Lenient : TermPackage.Standard;
    }

    /// <summary>The Accord tax rate for a contract performed under <paramref name="reach"/>: nobody collects it outside the Accord.</summary>
    public static decimal TaxRate(SimulationContext context, AccordReach reach) =>
        reach is AccordReach.DeepAccord or AccordReach.AccordEdge ? context.Config.TaxRate : 0m;

    private static PenaltyOnLateDelivery LateFee(Credits price, Package p, decimal scale) =>
        new(PenaltyPerTickLate: Math.Max(0.1m, Math.Round(price.Amount * p.LatePerHour * scale, 2)), MaxPenalty: Credits.Of(Math.Round(price.Amount * p.LateCap, 2)));

    private sealed record Package(
        long GraceHours,
        decimal LatePerHour,
        decimal LateCap,
        decimal BreakFee,
        PartialDelivery Partial,
        decimal MinQuality,
        decimal MaxRefund,
        decimal? RejectBelow,
        decimal LiabilityCap);
}

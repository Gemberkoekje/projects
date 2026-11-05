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
        BuildContract => ForBuild(offer.Package, offer.Price, offer.LateFeeScale),
        ShipSaleContract => ForShipSale(offer.Package),
        _ => contract.Terms,
    };

    // A Build's packages (design §3.4, §16.4): grace in days, the late fee per day of the price and its cap (the yard's bond).
    // The break fee is the deposit, for either side: the client forfeits it, the yard pays it back and as much again.
    private static readonly (long GraceHours, decimal LatePerDay, decimal LateCap)[] BuildPackages =
    {
        (3 * 24, 0.01m, 0.15m),
        (7 * 24, 0.005m, 0.10m),
        (14 * 24, 0.0025m, 0.05m),
    };

    /// <summary>
    /// The terms of a ship's Sale: it changes hands the moment it is signed, so nothing can be late and nothing is bonded; the
    /// buyer reads the ship's sheet and history before it signs (design §6.4), and takes it as it is.
    /// </summary>
    public static ContractTerms ForShipSale(TermPackage package) => new(package, 0, null, Credits.Zero, PartialDelivery.Failure, null, null);

    /// <summary>A standard Build's discount for a part one grade lower than ordered, as a share of the part's base price.</summary>
    public const decimal StandardSubstitutionDiscount = 0.20m;

    /// <summary>A lenient Build's discount per grade a part falls short, as a share of its base price: what the grade is worth (B is 85% of A).</summary>
    public const decimal LenientDiscountPerStep = 0.15m;

    /// <summary>
    /// The terms of a Build at <paramref name="price"/> (design §3.4, §16.4): the late fee runs per hour at a day's rate over
    /// 24, up to its cap; the break fee is the deposit. Its substitution policy is its package's (<see cref="Substitution"/>):
    /// <code>
    ///                          strict     standard          lenient
    ///   grace                  3 days     7 days            14 days
    ///   late fee per day       1%         0.5%              0.25%
    ///   late-fee cap (bond)    15%        10%               5%
    ///   break fee              the 20% deposit, either side
    ///   substitution           none       one grade lower,  any lower grade,
    ///                                     20% of the part   15% of the part a grade
    /// </code>
    /// </summary>
    public static ContractTerms ForBuild(TermPackage package, Credits price, decimal lateFeeScale = 1m)
    {
        (long grace, decimal perDay, decimal cap) = BuildPackages[(int)package];
        var lateFee = new PenaltyOnLateDelivery(
            PenaltyPerTickLate: Math.Max(0.1m, Math.Round(price.Amount * perDay / 24m * lateFeeScale, 2)), MaxPenalty: Credits.Of(Math.Round(price.Amount * cap, 2)));
        return new ContractTerms(package, grace, lateFee, BuildContract.DepositFor(price), PartialDelivery.Failure, null, null, lateFeeScale);
    }

    /// <summary>
    /// What a Build's substitution policy (design §3.4) makes of the parts fitted against those ordered: the discount owed, or
    /// null if a part falls outside it (another kind, or more grades short than it allows), so the client may reject the ship.
    /// Strict allows no substitute; standard one grade lower at <see cref="StandardSubstitutionDiscount"/>; lenient any lower
    /// grade, priced down <see cref="LenientDiscountPerStep"/> a grade.
    /// </summary>
    public static Credits? Substitution(TermPackage package, ShipParts ordered, ShipParts fitted)
    {
        decimal discount = 0m;
        foreach (PartSlot slot in Enum.GetValues<PartSlot>())
        {
            FittedPart? want = ordered.In(slot);
            FittedPart? got = fitted.In(slot);
            if (want is null && got is null)
            {
                continue;
            }

            if (want is not { } w || got is not { } g || w.Kind != g.Kind)
            {
                return null;
            }

            int steps = ShipPart.StepsShort(w.Grade, g.Grade);
            if (steps <= 0)
            {
                continue;
            }

            if (SubstituteDiscount(package, w, steps) is not { } owed)
            {
                return null;
            }

            discount += owed.Amount;
        }

        return Credits.Of(Math.Round(discount, 2));
    }

    /// <summary>The discount a policy owes for a part <paramref name="steps"/> grades short; null if it doesn't allow it.</summary>
    public static Credits? SubstituteDiscount(TermPackage package, FittedPart ordered, int steps)
    {
        decimal basePrice = ordered.Info.BasePrice.Amount;
        return package switch
        {
            TermPackage.Strict => null,
            TermPackage.Standard => steps == 1 ? Credits.Of(Math.Round(basePrice * StandardSubstitutionDiscount, 2)) : null,
            _ => Credits.Of(Math.Round(basePrice * LenientDiscountPerStep * steps, 2)),
        };
    }

    /// <summary>Whether a Build's policy lets a yard fit <paramref name="fitted"/> where <paramref name="ordered"/> was ordered, without asking.</summary>
    public static bool Allows(TermPackage package, FittedPart ordered, FittedPart fitted) =>
        ordered.Kind == fitted.Kind && (fitted.Grade <= ordered.Grade || SubstituteDiscount(package, ordered, ShipPart.StepsShort(ordered.Grade, fitted.Grade)) is not null);

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

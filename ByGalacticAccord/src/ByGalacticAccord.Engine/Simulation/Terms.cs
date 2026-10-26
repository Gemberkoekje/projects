using System;
using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The simple terms every Sale and Haul carries in S3 (plan S3; S4 brings the full set and the packages): a
/// deadline with no grace, a late fee per hour up to a cap, and for perishables a quality warranty. Every
/// figure is scaled from the posted price, never from the poster's hidden reservation, so the terms give
/// nothing away (design §3.6).
/// </summary>
public static class Terms
{
    /// <summary>A Sale's late fee per hour, as a share of its price.</summary>
    public const decimal SaleLatePerHour = 0.005m;

    /// <summary>A Sale's late-fee cap (and so the seller's bond), as a share of its price (design §3.4's example: 15%).</summary>
    public const decimal SaleLateCap = 0.15m;

    /// <summary>A Haul's late fee per hour, as a share of its fee.</summary>
    public const decimal HaulLatePerHour = 0.01m;

    /// <summary>A Haul's late-fee cap (and so the carrier's bond), as a share of its fee.</summary>
    public const decimal HaulLateCap = 0.3m;

    /// <summary>The most of the price a warranty refunds, for goods that arrive spoiled through.</summary>
    public const decimal WarrantyMaxRefund = 0.5m;

    public static IReadOnlyList<ContractClause> ForSale(GoodType good, Credits price) =>
        Standard(good, price, SaleLatePerHour, SaleLateCap);

    public static IReadOnlyList<ContractClause> ForHaul(GoodType good, Credits fee) =>
        Standard(good, fee, HaulLatePerHour, HaulLateCap);

    /// <summary>The Accord tax rate for a contract performed under <paramref name="reach"/>: nobody collects it outside the Accord.</summary>
    public static decimal TaxRate(SimulationContext context, AccordReach reach) =>
        reach is AccordReach.DeepAccord or AccordReach.AccordEdge ? context.Config.TaxRate : 0m;

    private static List<ContractClause> Standard(GoodType good, Credits price, decimal perHour, decimal cap)
    {
        var clauses = new List<ContractClause>
        {
            new PenaltyOnLateDelivery(
                PenaltyPerTickLate: Math.Max(0.1m, Math.Round(price.Amount * perHour, 2)),
                MaxPenalty: Credits.Of(Math.Round(price.Amount * cap, 2))),
        };

        if (Goods.Info(good).Perishable)
        {
            clauses.Add(new QualityWarranty(MinQuality: Goods.StandardWarranty, MaxRefundFraction: WarrantyMaxRefund));
        }

        return clauses;
    }
}

using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>
/// Turns supply and demand pressure into prices (§4.2.2). The whole skill of the game is reading
/// these signals (§1.2): an idle producer (high stock) asks less, widening the haul spread — the
/// player who sees that idle state first gets the lucrative run.
/// </summary>
public static class PriceModel
{
    /// <summary>
    /// Per-unit price a producer asks at the source. Anchored on production cost (cheap at origin)
    /// so the geographic arbitrage to a consumer's willingness leaves room for a haul fee. Oversupply
    /// still depresses it (§4.2.2): a full warehouse asks less, which is the player's idle-producer tell.
    /// </summary>
    public static Credits ProducerAsk(ProducerProfile producer, decimal currentStock)
    {
        decimal stockRatio = producer.StockCap <= 0m ? 0m : Math.Clamp(currentStock / producer.StockCap, 0m, 1m);

        // Full warehouse -> 0.8x; empty -> 1.3x of the cost-plus-margin anchor.
        decimal supplyFactor = 1.3m - (0.5m * stockRatio);
        decimal ask = producer.UnitProductionCost.Amount * (1m + producer.Margin) * supplyFactor;
        return Credits.Of(Math.Max(producer.UnitProductionCost.Amount, ask));
    }

    /// <summary>
    /// What a producer asks per unit, given its stock and its savings (design §5.2: from its cost, its margin and how
    /// full its warehouse is). A producer with more than twice a full warehouse's worth of production cost in hand
    /// gives up its margin, all of it by four times (its working capital): savings it can't use don't make it hold
    /// out for more.
    /// </summary>
    public static Credits ProducerAsk(ProducerProfile producer, decimal currentStock, Credits credits)
    {
        decimal reserve = Math.Max(1m, producer.UnitProductionCost.Amount * producer.StockCap);
        decimal marginShare = Math.Clamp(2m - (credits.Amount / (2m * reserve)), 0m, 1m);
        return ProducerAsk(producer with { Margin = producer.Margin * marginShare }, currentStock);
    }

    /// <summary>The most a consumer will pay per unit at the destination, given how short it is of its buffer.</summary>
    public static Credits ConsumerWillingness(GoodType good, decimal currentStock, decimal targetStock)
    {
        GoodInfo info = Goods.Info(good);
        decimal needRatio = targetStock <= 0m ? 0m : Math.Clamp((targetStock - currentStock) / targetStock, 0m, 1m);

        // Scarcity drives the price up: empty larder -> 1.6x base value, full -> 0.9x.
        decimal demandFactor = 0.9m + (0.7m * needRatio);
        return Credits.Of(info.BaseValue * demandFactor);
    }

    /// <summary>
    /// The most a consumer will pay per unit, given how short it is and what it can afford (design §5.2): a consumer
    /// with a month's income in hand pays as <see cref="ConsumerWillingness(GoodType, decimal, decimal)"/> says; one with
    /// more pays more, up to 1.6 times, and a broke one less, down to 0.85.
    /// </summary>
    public static Credits ConsumerWillingness(GoodType good, decimal currentStock, decimal targetStock, Credits credits, Credits incomePerDay)
    {
        decimal reserve = Math.Max(1m, incomePerDay.Amount * 30m);
        decimal affordability = Math.Clamp(0.85m + (0.15m * credits.Amount / reserve), 0.85m, 1.6m);
        return Credits.Of(ConsumerWillingness(good, currentStock, targetStock).Amount * affordability);
    }

    /// <summary>The pilot's floor fee for a haul: running cost over the route + a risk premium + a thin margin.</summary>
    public static Credits PilotMinimumFee(ShipClassInfo cls, long distanceTicks, decimal danger, Personality personality)
    {
        long travelTicks = TravelTicks(distanceTicks, cls.SpeedMultiplier);
        decimal runningCost = cls.RunningCostPerTick.Amount * travelTicks;

        // Risk premium: dangerous routes demand more, discounted by the pilot's risk tolerance.
        decimal riskPremium = runningCost * danger * (1.6m - personality.RiskTolerance);

        // Desired margin widens with liquidity preference (§4.1.6). Kept healthy so a working pilot
        // builds a capital buffer rather than grinding at the break-even floor.
        decimal margin = runningCost * (0.6m + (0.7m * personality.LiquidityPreference));
        return Credits.Of(runningCost + riskPremium + margin);
    }

    public static long TravelTicks(long distanceTicks, decimal speedMultiplier)
    {
        if (distanceTicks <= 0)
        {
            return 0;
        }

        decimal ticks = distanceTicks / speedMultiplier;
        return Math.Max(1, (long)Math.Ceiling(ticks));
    }
}

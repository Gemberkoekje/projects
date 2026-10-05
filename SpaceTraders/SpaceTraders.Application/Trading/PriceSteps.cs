namespace SpaceTraders.Application.Trading;

/// <summary>
/// How much a market's price rises with each of our purchases, to estimate what units bought in batches cost (D81). A market
/// trades at most its trade volume in one transaction, the most a single purchase takes, not its stock: more is bought in
/// several purchases, each at the price quoted then, and each purchase raises the next quote.
/// </summary>
/// <remarks>
/// Measured on 2026-10-05 from the bot's 222 purchases since the reset of 2026-10-04, each against the market as seen just
/// before and just after it (<c>market_price_samples</c>): the price paid was the price quoted (median difference 0.00%), and
/// the next quote was higher by a median of 1.8% after a full batch of a good the market trades 6 at a time, 3.6% after one of
/// 20, and 5.7% after 40 of 60. A raised price was back within 1% after a median of 56 minutes. The steps here are those,
/// rounded up: an estimate the purchase checks against the price it is quoted before each batch.
/// </remarks>
public static class PriceSteps
{
    /// <summary>How much the next quote rises after a purchase of a full batch, as a fraction of the price.</summary>
    /// <param name="tradeVolume">What the market trades at once.</param>
    /// <returns>0.02 up to 6 at a time, 0.04 up to 20, 0.06 above.</returns>
    public static double PurchaseStep(int tradeVolume) => tradeVolume switch
    {
        <= 6 => 0.02,
        <= 20 => 0.04,
        _ => 0.06,
    };

    /// <summary>
    /// What units are expected to cost bought in batches of the market's trade volume: the first at the price quoted now, each
    /// further batch a step dearer than the one before.
    /// </summary>
    /// <param name="unitPrice">What a unit costs at the market now.</param>
    /// <param name="units">The units to buy.</param>
    /// <param name="tradeVolume">What the market trades at once; one batch when it names none.</param>
    /// <returns>The expected cost, rounded up.</returns>
    public static long CostInBatches(long unitPrice, int units, int tradeVolume)
    {
        var batch = tradeVolume > 0 ? tradeVolume : Math.Max(1, units);
        var step = PurchaseStep(batch);
        var cost = 0.0;
        var price = (double)unitPrice;
        for (var left = units; left > 0; left -= batch)
        {
            cost += Math.Min(left, batch) * price;
            price *= 1 + step;
        }

        return (long)Math.Ceiling(cost);
    }
}

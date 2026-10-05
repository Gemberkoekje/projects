namespace SpaceTraders.Application.Trading;

/// <summary>
/// How much a market's price moves with each of our trades, to estimate what units bought or sold in batches cost or fetch
/// (D79, D81). A market trades at most its trade volume in one transaction, the most a single purchase or sale takes, not its
/// stock: more goes in several, each at the price quoted then, and each purchase raises the next quote, each sale lowers it.
/// </summary>
/// <remarks>
/// Measured on 2026-10-05 from the bot's 222 purchases since the reset of 2026-10-04, each against the market as seen just
/// before and just after it (<c>market_price_samples</c>): the price paid was the price quoted (median difference 0.00%), and
/// the next quote was higher by a median of 1.8% after a full batch of a good the market trades 6 at a time, 3.6% after one of
/// 20, and 5.7% after 40 of 60. A sale of a full batch lowered it by a median of 1.8% (6 at a time) and 2.1% (20); the bot
/// sold no full batch of more. A raised price was back within 1% after a median of 56 minutes. The steps here are those,
/// rounded: an estimate each purchase and sale checks against the price it is quoted before each batch.
/// </remarks>
public static class PriceSteps
{
    /// <summary>How much the next quote falls after a sale of a full batch, as a fraction of the price.</summary>
    public const double SaleStep = 0.02;

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
    /// What a unit is expected to cost when bought as the given one of a run of purchases: the first batch at the price quoted
    /// now, each further batch a step dearer than the one before.
    /// </summary>
    /// <param name="quote">What a unit costs at the market now.</param>
    /// <param name="unit">The unit's place in the run, from 0.</param>
    /// <param name="tradeVolume">What the market trades at once; one batch when it names none.</param>
    /// <returns>The expected price of that unit.</returns>
    public static double PurchasePriceOf(long quote, int unit, int tradeVolume)
        => tradeVolume > 0 ? quote * Math.Pow(1 + PurchaseStep(tradeVolume), BatchOf(unit, tradeVolume)) : quote;

    /// <summary>
    /// What a unit is expected to fetch when sold as the given one of a run of sales: the first batch at the price quoted now,
    /// each further batch a step cheaper than the one before.
    /// </summary>
    /// <param name="quote">What a unit fetches at the market now.</param>
    /// <param name="unit">The unit's place in the run, from 0.</param>
    /// <param name="tradeVolume">What the market trades at once; one batch when it names none.</param>
    /// <returns>The expected price of that unit.</returns>
    public static double SalePriceOf(long quote, int unit, int tradeVolume)
        => tradeVolume > 0 ? quote * Math.Pow(1 - SaleStep, BatchOf(unit, tradeVolume)) : quote;

    /// <summary>The batch a unit falls in, from 0: whole batches of the trade volume before it.</summary>
    private static int BatchOf(int unit, int tradeVolume) => unit / tradeVolume;

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
        var cost = 0.0;
        for (var unit = 0; unit < units; unit++)
        {
            cost += PurchasePriceOf(unitPrice, unit, tradeVolume);
        }

        // An exact sum must not round up by one for a sliver of floating-point error.
        return (long)Math.Ceiling(cost - 1e-6);
    }
}

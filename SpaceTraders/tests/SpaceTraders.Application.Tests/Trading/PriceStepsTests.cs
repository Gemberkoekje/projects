using FluentAssertions;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// D81: units bought in batches of a market's trade volume, the most one purchase takes, each purchase raising the next quote
/// by the step measured on 2026-10-05: a median of 1.8%, 3.6% and 5.7% for a good traded 6, 20 and 60 at a time, rounded up.
/// </summary>
public sealed class PriceStepsTests
{
    [Theory]
    [InlineData(6, 0.02)]
    [InlineData(20, 0.04)]
    [InlineData(60, 0.06)]
    [InlineData(180, 0.06)]
    public void EachPurchaseOfAFullBatch_RaisesTheNextQuote_ByTheMeasuredStep(int tradeVolume, double step)
        => PriceSteps.PurchaseStep(tradeVolume).Should().Be(step);

    [Fact]
    public void UnitsInSeveralBatches_CostEachBatchAStepMoreThanTheOneBefore()
    {
        // ADVANCED_CIRCUITRY at X1-FJ91-D49 on 2026-10-05: 3,578, 20 at a time. A hauler's 80 are four batches: 71,560,
        // 74,422.40, 77,399.30 and 80,495.27.
        PriceSteps.CostInBatches(3_578, 80, 20).Should().Be(303_877);
    }

    [Fact]
    public void ALastBatchSmallerThanTheTradeVolume_CostsOnlyItsUnits()
        => PriceSteps.CostInBatches(1_000, 25, 20).Should().Be((20 * 1_000) + (5 * 1_040));

    [Fact]
    public void WhatOnePurchaseTakes_CostsThePriceQuoted()
    {
        PriceSteps.CostInBatches(2_100, 80, 80).Should().Be(168_000);
        PriceSteps.CostInBatches(2_100, 80, 0).Should().Be(168_000, "a market that names no trade volume is taken to sell it in one go");
    }
}

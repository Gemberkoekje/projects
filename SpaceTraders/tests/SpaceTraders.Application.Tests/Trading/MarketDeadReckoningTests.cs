using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.46 (D131), asked on 2026-11-08: "Can we make a dead reckoning prediction rule for any markets without active ships or
/// probes so that our command ship goes there to trade eventually? Even if it isn't fully accurate (e.g. due to other players) at
/// least it will solve the problem." A price drifts back towards the good's resting price at the market (the median of its
/// samples), the supply to its usual level, and the trade volume stays as seen.
/// </summary>
public sealed class MarketDeadReckoningTests
{
    private static readonly DateTimeOffset LastLook = new(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnExportPriceOurPurchasesRaised_DriftsBackToItsRestingPrice_TheGapShrinkingWithTheTimeConstant()
    {
        // On 2026-11-03 the circuitry at X1-UK3-D45 stood around 6,500 and each 20-unit purchase raised it about 9%, to 7,105.
        var good = new TradeGoodSnapshot("ADVANCED_CIRCUITRY", "EXPORT", 7_105, 3_400, 20, "SCARCE");
        var history = Samples(("ADVANCED_CIRCUITRY", 6_494, 3_100, "LIMITED"), ("ADVANCED_CIRCUITRY", 6_500, 3_110, "LIMITED"), ("ADVANCED_CIRCUITRY", 7_105, 3_400, "SCARCE"));
        var tau = MarketDeadReckoning.ExportTimeConstant;

        var atOnce = MarketDeadReckoning.Predict(good, history, TimeSpan.Zero);
        var oneTau = MarketDeadReckoning.Predict(good, history, tau);
        var later = MarketDeadReckoning.Predict(good, history, 20 * tau);

        atOnce.Should().Be(good, "nothing has drifted yet");
        var expected = Math.Exp(Math.Log(6_500) + ((Math.Log(7_105) - Math.Log(6_500)) * Math.Exp(-1)));
        oneTau.PurchasePrice.Should().BeCloseTo((int)Math.Round(expected), 1);
        oneTau.PurchasePrice.Should().BeInRange(6_501, 7_104);
        later.PurchasePrice.Should().Be(6_500, "the median of its samples");
        later.SellPrice.Should().Be(3_110);
        later.Supply.Should().Be("LIMITED", "the level it showed most often");
        later.TradeVolume.Should().Be(20, "the trade volume stays as seen");
        later.Type.Should().Be("EXPORT");
    }

    [Fact]
    public void AGoodWithNoSamples_StaysAsSeen()
    {
        var good = new TradeGoodSnapshot("EQUIPMENT", "IMPORT", 3_000, 1_400, 40, "MODERATE");

        MarketDeadReckoning.Predict(good, [], TimeSpan.FromHours(18)).Should().Be(good);
    }

    [Fact]
    public void AMarket_IsPredictedGoodByGood_FromEachGoodsOwnSamples()
    {
        var market = new MarketSnapshot(
            "X1-UK3-K88",
            "X1-UK3",
            [
                new TradeGoodSnapshot("CLOTHING", "EXPORT", 3_400, 1_600, 20, "LIMITED"),
                new TradeGoodSnapshot("EQUIPMENT", "EXPORT", 2_400, 1_100, 20, "LIMITED"),
            ],
            [],
            ["CLOTHING", "EQUIPMENT"],
            []);
        var history = Samples(("CLOTHING", 2_950, 1_400, "MODERATE"), ("CLOTHING", 3_400, 1_600, "LIMITED"), ("CLOTHING", 2_950, 1_400, "MODERATE"), ("EQUIPMENT", 1_887, 900, "MODERATE"));

        var predicted = MarketDeadReckoning.Predict(market, history, TimeSpan.FromHours(18));

        predicted.TradeGoods.Select(good => (good.Symbol, good.PurchasePrice, good.Supply))
            .Should().Equal(("CLOTHING", 2_950, "MODERATE"), ("EQUIPMENT", 1_887, "MODERATE"));
        predicted.Exports.Should().Equal("CLOTHING", "EQUIPMENT");
    }

    private static List<MarketPriceSampleDto> Samples(params (string Good, int Purchase, int Sell, string Supply)[] samples)
        => [.. samples.Select((sample, index) => new MarketPriceSampleDto(
            LastLook.AddMinutes(-10 * (samples.Length - 1 - index)),
            "X1-UK3-D45",
            sample.Good,
            sample.Purchase,
            sample.Sell,
            sample.Supply,
            "WEAK",
            20))];
}

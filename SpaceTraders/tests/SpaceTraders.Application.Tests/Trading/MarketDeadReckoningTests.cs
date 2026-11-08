using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.46 (D131), asked on 2026-11-08: "Can we make a dead reckoning prediction rule for any markets without active ships or
/// probes so that our command ship goes there to trade eventually? Even if it isn't fully accurate (e.g. due to other players) at
/// least it will solve the problem." Fitted on the runs of 2026-10-11 to 2026-11-01: an export drifts down to the lowest price
/// it showed this run (τ 16 hours), an import up to the highest of the last 24 hours (τ 4 hours), with few looks bounded by the
/// supply ladder; the sell price follows, the supply moves after 4 hours, the trade volume and an exchanged good stay as seen.
/// </summary>
public sealed class MarketDeadReckoningTests
{
    private static readonly DateTimeOffset LastLook = new(2026, 11, 02, 07, 00, 00, TimeSpan.Zero);

    [Fact]
    public void AnExportOurPurchasesRaised_DriftsBackToTheLowestPriceItShowedThisRun_WithATimeConstantOf16Hours()
    {
        // On 2026-11-03 the circuitry at X1-UK3-D45 stood around 6,494 and a 20-unit purchase raised it about 9%, to 7,105.
        var good = new TradeGoodSnapshot("ADVANCED_CIRCUITRY", "EXPORT", 7_105, 3_552, 20, "SCARCE");
        var history = Samples("ADVANCED_CIRCUITRY", (-40, 6_494), (-30, 6_638), (-20, 6_787), (-10, 6_943), (0, 7_105));
        var hours = MarketDeadReckoning.ExportTimeConstant.TotalHours;

        var atOnce = MarketDeadReckoning.Predict(good, history, LastLook, TimeSpan.Zero);
        var oneTau = MarketDeadReckoning.Predict(good, history, LastLook, MarketDeadReckoning.ExportTimeConstant);
        var later = MarketDeadReckoning.Predict(good, history, LastLook, TimeSpan.FromHours(20 * hours));

        hours.Should().Be(16);
        atOnce.Should().Be(good, "nothing has drifted yet");
        var expected = Math.Exp(Math.Log(6_494) + ((Math.Log(7_105) - Math.Log(6_494)) * Math.Exp(-1)));
        oneTau.PurchasePrice.Should().BeCloseTo((int)Math.Round(expected), 1);
        oneTau.SellPrice.Should().BeCloseTo((int)Math.Round(3_552.0 * oneTau.PurchasePrice / 7_105), 1, "the sell price moves with it");
        later.PurchasePrice.Should().Be(6_494, "the lowest price it showed this run");
        later.Supply.Should().Be("ABUNDANT", "back at its resting price, nobody buying, its supply has grown");
        later.TradeVolume.Should().Be(20, "the trade volume stays as seen");
    }

    [Fact]
    public void AnImportOurSalesLowered_DriftsUpToTheHighestPriceOfTheLast24Hours_WithATimeConstantOf4Hours()
    {
        // A look two days ago at 2,600 is too old to count; within the day it stood at 2,400 at most.
        var good = new TradeGoodSnapshot("MICROPROCESSORS", "IMPORT", 2_100, 1_050, 40, "HIGH");
        var history = Samples("MICROPROCESSORS", (-48 * 60, 2_600), (-20 * 60, 2_400), (-12 * 60, 2_350), (-6 * 60, 2_300), (-60, 2_200), (0, 2_100));

        var oneTau = MarketDeadReckoning.Predict(good, history, LastLook, MarketDeadReckoning.ImportTimeConstant);
        var soon = MarketDeadReckoning.Predict(good, history, LastLook, TimeSpan.FromHours(3));
        var later = MarketDeadReckoning.Predict(good, history, LastLook, TimeSpan.FromHours(80));

        MarketDeadReckoning.ImportTimeConstant.TotalHours.Should().Be(4);
        var expected = Math.Exp(Math.Log(2_400) + ((Math.Log(2_100) - Math.Log(2_400)) * Math.Exp(-1)));
        oneTau.PurchasePrice.Should().BeCloseTo((int)Math.Round(expected), 1);
        later.PurchasePrice.Should().Be(2_400);
        soon.Supply.Should().Be("HIGH", "the supply stays as seen for 4 hours");
        later.Supply.Should().Be("SCARCE", "nobody selling, the import has used up what it had");
    }

    [Fact]
    public void WithFewLooks_TheSupplyAtTheLookBoundsTheRestingPrice()
    {
        // The scout's first look, then nothing: an export seen SCARCE at 3,400 stood about e^0.63 above its resting price.
        var good = new TradeGoodSnapshot("CLOTHING", "EXPORT", 3_400, 1_700, 20, "SCARCE");

        var later = MarketDeadReckoning.Predict(good, Samples("CLOTHING", (0, 3_400)), LastLook, TimeSpan.FromDays(10));

        later.PurchasePrice.Should().BeCloseTo((int)Math.Round(3_400 / Math.Exp(MarketDeadReckoning.ExportLadder[0])), 1);
    }

    [Fact]
    public void AnExchangedGood_AndAGoodWithAnUnknownSupply_StayAsSeen()
    {
        var exchanged = new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE");
        var unknown = new TradeGoodSnapshot("EQUIPMENT", "EXPORT", 3_000, 1_400, 40, string.Empty);

        MarketDeadReckoning.Predict(exchanged, Samples("FUEL", (-60, 90), (0, 72)), LastLook, TimeSpan.FromHours(18)).Should().Be(exchanged);
        MarketDeadReckoning.Predict(unknown, Samples("EQUIPMENT", (-60, 2_000), (0, 3_000)), LastLook, TimeSpan.FromHours(18)).Should().Be(unknown);
    }

    [Fact]
    public void AMarket_IsPredictedGoodByGood_FromEachGoodsOwnSamples()
    {
        var market = new MarketSnapshot(
            "X1-UK3-K88",
            "X1-UK3",
            [
                new TradeGoodSnapshot("CLOTHING", "EXPORT", 3_400, 1_600, 20, "LIMITED"),
                new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE"),
            ],
            [],
            ["CLOTHING"],
            ["FUEL"]);
        var history = Samples("CLOTHING", (-50, 2_950), (-40, 2_950), (-30, 2_950), (-20, 2_950), (0, 3_400))
            .Concat(Samples("FUEL", (-50, 90), (0, 72)))
            .ToList();

        var predicted = MarketDeadReckoning.Predict(market, MarketDeadReckoning.Summarize(history, LastLook), TimeSpan.FromDays(10));

        predicted.TradeGoods.Select(good => (good.Symbol, good.PurchasePrice)).Should().Equal(("CLOTHING", 2_950), ("FUEL", 72));
        predicted.Exports.Should().Equal("CLOTHING");
    }

    private static List<MarketPriceSampleDto> Samples(string good, params (int Minutes, int Purchase)[] looks)
        => [.. looks.Select(look => new MarketPriceSampleDto(
            LastLook.AddMinutes(look.Minutes),
            "X1-UK3-D45",
            good,
            look.Purchase,
            look.Purchase / 2,
            "MODERATE",
            "WEAK",
            20))];
}

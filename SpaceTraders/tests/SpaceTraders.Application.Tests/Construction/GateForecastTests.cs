using FluentAssertions;
using SpaceTraders.Application.Construction;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Construction;

/// <summary>
/// Slice 6.55 (D143), asked on 2026-11-09 for rule 3, "Any ship bought should be bought on the idea that buying the ship will
/// finish the gate faster than without it", "forecast first": when the gate will be finished, by the slowest of money, supply
/// and haul. F49 sells FAB_MATS at 2,100, 80 at a time; D42 ADVANCED_CIRCUITRY at 4,500, 40 at a time.
/// </summary>
public sealed class GateForecastTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 09, 10, 00, 00, TimeSpan.Zero);

    private static readonly MaterialNeed[] Unbuilt = [new("FAB_MATS", 1_600, 0, 0), new("ADVANCED_CIRCUITRY", 400, 0, 0)];

    [Fact]
    public void WithFewCredits_MoneyIsTheLimit()
    {
        // The units cost 1,600 x 2,100 x 1.25 + 400 x 4,500 x 1.25 = 6,450,000, of which 100,000 can be spent now: at 150,000
        // an hour, 42.33 hours. The sellers give 160 FAB_MATS (10 hours) and 80 ADVANCED_CIRCUITRY (5 hours) an hour; an 80-unit
        // hold every 15 minutes carries the 2,000 units in 6.25 hours.
        var forecast = GateForecast.Of(Map(), Unbuilt, SystemSymbol, 100_000, 150_000, 80, new Dictionary<string, double>(), Now);

        (forecast.EtaHours, forecast.MoneyHours, forecast.SupplyHours, forecast.HaulHours).Should().Be((42.33, 42.33, 10, 6.25));
        forecast.Binding.Should().Be(GateForecast.Money);
        forecast.RemainingCost.Should().Be(6_450_000);
        forecast.Materials.Select(material => (material.TradeSymbol, material.SupplyPerHour, material.SupplyHours))
            .Should().Equal(("FAB_MATS", 160, 10), ("ADVANCED_CIRCUITRY", 80, 5));
        forecast.ComputedAt.Should().Be(Now);
    }

    [Fact]
    public void WithEnoughEarnings_TheSlowestSellerIsTheLimit_AtThePaceSeenWhenThatIsFaster()
    {
        // 10,000,000 an hour pays for it in 0.64 hours. FAB_MATS is bought at 400 an hour lately: 4 hours; ADVANCED_CIRCUITRY
        // at its trade volumes, 5 hours.
        var forecast = GateForecast.Of(Map(), Unbuilt, SystemSymbol, 0, 10_000_000, 320, new Dictionary<string, double> { ["FAB_MATS"] = 400 }, Now);

        forecast.Binding.Should().Be(GateForecast.Supply);
        forecast.EtaHours.Should().Be(5);
        forecast.Materials.Single(material => material.TradeSymbol == "FAB_MATS").SupplyHours.Should().Be(4);
    }

    [Fact]
    public void WithoutEarnings_OrABuilder_ALimitReadsNever()
    {
        var forecast = GateForecast.Of(Map(), Unbuilt, SystemSymbol, 0, 0, 0, new Dictionary<string, double>(), Now);

        (forecast.MoneyHours, forecast.HaulHours, forecast.EtaHours).Should().Be((GateForecast.Never, GateForecast.Never, GateForecast.Never));
    }

    [Fact]
    public void WithEveryUnitDeliveredOrOnItsWay_TheGateIsDone()
    {
        var forecast = GateForecast.Of(Map(), [new("FAB_MATS", 1_600, 1_560, 40)], SystemSymbol, 0, 0, 0, new Dictionary<string, double>(), Now);

        forecast.Binding.Should().Be(GateForecast.Done);
        forecast.EtaHours.Should().Be(0);
    }
}

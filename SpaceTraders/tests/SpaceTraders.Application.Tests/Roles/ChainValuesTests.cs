using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// D39: "mining iron ore isn't that profitable by itself, but iron ore getting refined to iron getting made to machinery
/// is very profitable." A good sold to a market that makes something pricier from it counts a share of the price
/// difference, and that share again for the step after; fully while the market is SCARCE of it, not at ABUNDANT.
/// </summary>
public sealed class ChainValuesTests
{
    private const string Refinery = "X1-AB-R1";
    private const string Factory = "X1-AB-F1";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> MadeFrom = new Dictionary<string, IReadOnlyList<string>>
    {
        ["IRON"] = ["IRON_ORE"],
        ["MACHINERY"] = ["IRON"],
    };

    [Fact]
    public void IronOre_SoldToARefinery_CountsAShareOfIronAndAShareOfThatOfMachinery()
    {
        // The refinery is LIMITED in ore (three quarters) and charges 300 for iron against 118 for ore: 136.5. The factory
        // is SCARCE in iron (fully) and charges 2,000 for machinery against 400 for iron: 1,600. Half of 136.5, and half
        // of half of 1,600 on top, both steps scaled by the refinery's supply.
        var chains = new ChainValues(Map("LIMITED", "SCARCE"), share: 0.5);

        chains.PerUnit(Refinery, "IRON_ORE").Should().BeApproximately(0.5 * (136.5 + (0.5 * 1_600)), 0.001);
    }

    [Fact]
    public void AMarketThatIsAbundantInTheGood_AddsNothing_HoweverMuchMoreTheNextStepIsWorth()
    {
        new ChainValues(Map("ABUNDANT", "SCARCE"), share: 0.5).PerUnit(Refinery, "IRON_ORE").Should().Be(0);
    }

    [Fact]
    public void TheStepAfter_CountsLessAsItsMarketHasMoreOfItsInput()
    {
        var scarce = new ChainValues(Map("SCARCE", "SCARCE"), share: 0.5).PerUnit(Refinery, "IRON_ORE");
        var moderate = new ChainValues(Map("SCARCE", "MODERATE"), share: 0.5).PerUnit(Refinery, "IRON_ORE");
        var abundant = new ChainValues(Map("SCARCE", "ABUNDANT"), share: 0.5).PerUnit(Refinery, "IRON_ORE");

        scarce.Should().BeApproximately(0.5 * (182 + (0.5 * 1_600)), 0.001);
        moderate.Should().BeApproximately(0.5 * (182 + (0.5 * 0.5 * 1_600)), 0.001);
        abundant.Should().BeApproximately(0.5 * 182, 0.001);
    }

    [Fact]
    public void AShareOfNothing_AndAMarketThatMakesNothingFromIt_AddNothing()
    {
        new ChainValues(Map("SCARCE", "SCARCE"), share: 0).PerUnit(Refinery, "IRON_ORE").Should().Be(0);

        // The factory doesn't import iron ore; the refinery exports iron, it doesn't import it.
        var chains = new ChainValues(Map("SCARCE", "SCARCE"), share: 0.5);
        chains.PerUnit(Factory, "IRON_ORE").Should().Be(0);
        chains.PerUnit(Refinery, "IRON").Should().Be(0);
    }

    [Theory]
    [InlineData("SCARCE", 1)]
    [InlineData("LIMITED", 0.75)]
    [InlineData("MODERATE", 0.5)]
    [InlineData("HIGH", 0.25)]
    [InlineData("ABUNDANT", 0)]
    [InlineData("", 0)]
    public void ASupplyLevel_CountsAStep_FullyAtScarceAndNotAtAbundant(string supply, double factor)
    {
        ChainValues.SupplyFactor(supply).Should().Be(factor);
    }

    private static TradeMarketMap Map(string refineryOreSupply, string factoryIronSupply)
        => new(
            [Waypoint(Refinery, 0, 0), Waypoint(Factory, 30, 40)],
            [
                Market(Refinery, new TradeGoodSnapshot("IRON_ORE", "IMPORT", 118, 58, 60, refineryOreSupply), new TradeGoodSnapshot("IRON", "EXPORT", 300, 150, 60, "MODERATE")),
                Market(Factory, new TradeGoodSnapshot("IRON", "IMPORT", 400, 200, 60, factoryIronSupply), new TradeGoodSnapshot("MACHINERY", "EXPORT", 2_000, 1_000, 20, "MODERATE")),
            ],
            MadeFrom);

    private static MarketSnapshot Market(string waypoint, params TradeGoodSnapshot[] goods)
        => new(
            waypoint,
            "X1-AB",
            goods,
            [.. goods.Where(good => good.Type == "IMPORT").Select(good => good.Symbol)],
            [.. goods.Where(good => good.Type == "EXPORT").Select(good => good.Symbol)],
            []);

    private static WaypointCacheModel Waypoint(string symbol, int x, int y)
        => new(symbol, "X1-AB", "PLANET", x, y, true, false, DateTimeOffset.UnixEpoch);
}

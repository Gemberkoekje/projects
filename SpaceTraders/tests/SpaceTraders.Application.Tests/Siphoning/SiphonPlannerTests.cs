using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Siphoning;

/// <summary>
/// Slice 6.7: what a siphoner siphons, by the miners' rules (D28) for gases: the markets shortest of a gas
/// first, siphoned at the gas giant nearest the market and sold there; no surveys, as a siphon takes none.
/// </summary>
public sealed class SiphonPlannerTests
{
    [Fact]
    public void ASiphoner_ServesTheMarketsShortestOfAGasFirst_AndSellsThere()
    {
        // D28 for gases: G50's SCARCE hydrogen and E47's SCARCE nitrogen, then G50's LIMITED hydrocarbon, which pays
        // most, then the MODERATE markets; C39, next to the gas giant, last but for what it pays. F48 is beyond a
        // drone's tank in this fixture.
        var targets = SiphonPlanner.SiphonTargets(Map(), SiphonDrone(), new HashSet<string>());

        targets.Select(target => (target.Gas, target.SellWaypointSymbol, target.Supply)).Should().Equal(
            ("LIQUID_HYDROGEN", G50, "SCARCE"),
            ("LIQUID_NITROGEN", E47, "SCARCE"),
            ("HYDROCARBON", G50, "LIMITED"),
            ("HYDROCARBON", C39, "MODERATE"),
            ("LIQUID_HYDROGEN", E47, "MODERATE"),
            ("LIQUID_NITROGEN", G50, "MODERATE"),
            ("LIQUID_HYDROGEN", C39, "MODERATE"),
            ("LIQUID_NITROGEN", C39, "MODERATE"));
        targets.Should().OnlyContain(target => target.GasGiantSymbol == C38);
        targets.Select(target => target.LowSupply).Should().Equal(true, true, true, false, false, false, false, false);
    }

    [Fact]
    public void ASiphon_YieldsAnyOfTheGasGiantsThreeGases_AboutEqually()
    {
        var target = SiphonPlanner.SiphonTargets(Map(), SiphonDrone(), new HashSet<string>())[0];

        target.Should().BeEquivalentTo(new SiphonTarget("LIQUID_HYDROGEN", C38, G50, 55, 1 / 3.0, "SCARCE"));
        target.ExpectedValue.Should().BeApproximately(55 / 3.0, 1e-9);
    }

    [Fact]
    public void AnOpeningAnotherSiphonerHolds_IsNotOffered()
    {
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(G50, "LIQUID_HYDROGEN") };

        var targets = SiphonPlanner.SiphonTargets(Map(), SiphonDrone(), held);

        targets.Should().NotContain(target => target.Key == MiningPlanner.OpportunityKey(G50, "LIQUID_HYDROGEN"));
        (targets[0].Gas, targets[0].SellWaypointSymbol).Should().Be(("LIQUID_NITROGEN", E47));
    }

    [Fact]
    public void AScarceGasNoSiphonerWorksOn_ComesFirst_ThoughAGasASiphonerWorksOnIsShorter()
    {
        // Slice 6.10b (D48): C39 is SCARCE of hydrocarbon here, and pays most for it, but a siphoner works on hydrocarbon
        // for G50. Hydrogen and nitrogen have nobody.
        MarketSnapshot[] markets =
        [
            .. Markets().Where(market => market.WaypointSymbol != C39),
            Market(
                C39,
                Good("HYDROCARBON", "EXCHANGE", 70, 60, 60, "SCARCE"),
                Good("LIQUID_HYDROGEN", "EXCHANGE", 40, 35, 60, "MODERATE"),
                Good("LIQUID_NITROGEN", "EXCHANGE", 34, 30, 60, "MODERATE"),
                Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
        ];
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(G50, "HYDROCARBON") };

        var targets = SiphonPlanner.SiphonTargets(Map(markets), SiphonDrone(), held, new HashSet<string> { "HYDROCARBON" });

        targets.Select(target => (target.Gas, target.SellWaypointSymbol)).Take(3).Should().Equal(
            ("LIQUID_HYDROGEN", G50),
            ("LIQUID_NITROGEN", E47),
            ("HYDROCARBON", C39));
    }

    [Fact]
    public void TheScarceGases_AreThoseADroneCouldServeAMarketShortOf()
    {
        SiphonPlanner.ScarceGases(Map(), SiphonDrone()).Should().BeEquivalentTo(["LIQUID_HYDROGEN", "LIQUID_NITROGEN", "HYDROCARBON"]);
    }

    [Fact]
    public void ALongRangeShip_ReachesTheFarMarkets()
    {
        var targets = SiphonPlanner.SiphonTargets(Map(), CommandShip(), new HashSet<string>());

        (targets[0].Gas, targets[0].SellWaypointSymbol).Should().Be(("LIQUID_NITROGEN", F48), "F48's scarce nitrogen pays most of the scarce gases");
        targets.Should().Contain(target => target.Gas == "LIQUID_HYDROGEN" && target.SellWaypointSymbol == F48);
    }

    [Fact]
    public void WithoutAGasGiant_ThereIsNothingToSiphon()
    {
        var map = new TradeMarketMap(
            Waypoints.Where(waypoint => waypoint.Symbol != C38),
            Markets(),
            new Dictionary<string, IReadOnlyList<string>>());

        SiphonPlanner.SiphonTargets(map, SiphonDrone(), new HashSet<string>()).Should().BeEmpty();
        SiphonPlanner.LowSupplyOpportunities(map).Should().BeEmpty();
    }

    [Fact]
    public void TheLowSupplyOpenings_AreEveryMarketWithAGasInLowSupply_WithTheGasGiantNearestIt()
    {
        var openings = SiphonPlanner.LowSupplyOpportunities(Map());

        openings.Select(opening => (opening.SellWaypointSymbol, opening.Gas, opening.GasGiantSymbol)).Should().BeEquivalentTo(
        [
            (E47, "LIQUID_NITROGEN", C38),
            (F48, "LIQUID_HYDROGEN", C38),
            (F48, "LIQUID_NITROGEN", C38),
            (G50, "HYDROCARBON", C38),
            (G50, "LIQUID_HYDROGEN", C38),
        ]);
    }

    [Fact]
    public void OresAreNotSiphoned_AndGasesNotMined()
    {
        // The two plans never offer each other's goods: a market that buys both lists each in its own plan.
        var map = Map(
        [
            .. Markets().Where(market => market.WaypointSymbol is not G50 and not E47 and not C39),
            Market(G50, Good("COPPER_ORE", "IMPORT", 130, 65, 60, "SCARCE"), Good("HYDROCARBON", "IMPORT", 180, 90, 60, "LIMITED"), Good("FUEL", "EXCHANGE", 82, 72, 180, "MODERATE")),
        ]);

        SiphonPlanner.SiphonTargets(map, SiphonDrone(), new HashSet<string>()).Select(target => target.Gas).Should().Equal("HYDROCARBON");
        AsteroidDeposits.Ores.Should().NotIntersectWith(GasGiants.Gases);
    }

    [Fact]
    public void OnlyAGasGiant_CanBeSiphoned()
    {
        var c38 = Waypoints.Single(waypoint => waypoint.Symbol == C38);
        var c39 = Waypoints.Single(waypoint => waypoint.Symbol == C39);

        GasGiants.GasesAt(c38).Should().Equal("HYDROCARBON", "LIQUID_HYDROGEN", "LIQUID_NITROGEN");
        GasGiants.GasesAt(c39).Should().BeEmpty();
        GasGiants.CanYield(c38, "LIQUID_NITROGEN").Should().BeTrue();
        GasGiants.CanYield(c38, "COPPER_ORE").Should().BeFalse();
    }
}

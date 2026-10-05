using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Siphoning;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Siphoning.SiphonFixture;

namespace SpaceTraders.Application.Tests.Siphoning;

/// <summary>
/// Slice 6.7: what a siphoner siphons, by the miners' rules (D28) for gases: the markets shortest of a gas
/// first, siphoned at the gas giant nearest the market and sold there; no surveys, as a siphon takes none. D77: no market
/// that has the gas ABUNDANT, and a drone shares a pair once every pair below ABUNDANT has a siphoner.
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

    [Theory]
    [InlineData("HIGH", true)]
    [InlineData("ABUNDANT", false)]
    public void AMarketThatHasAllTheGasItWants_IsNoSiphonTarget(string supply, bool isTarget)
    {
        // D77, asked on 2026-10-05, for siphon drones too: they siphon until every gas is ABUNDANT. At ABUNDANT, C39 has all
        // the hydrocarbon it wants; at HIGH it is still the lowest supply left to siphon for (D28).
        var targets = SiphonPlanner.SiphonTargets(Map(MarketsWithHydrocarbonAtC39(supply)), SiphonDrone(), new HashSet<string>());

        targets.Any(target => target.Gas == "HYDROCARBON" && target.SellWaypointSymbol == C39).Should().Be(isTarget);
        targets.Should().Contain(target => target.Gas == "LIQUID_HYDROGEN" && target.SellWaypointSymbol == C39, "C39's other gases still count");
    }

    [Fact]
    public void ASiphonDroneThatShares_TakesTheLowestSupply_ThenThePairWithTheFewestSiphoners()
    {
        // D77: once every pair below ABUNDANT has a siphoner, a drone shares one rather than trade. G50's hydrogen has two
        // drones and E47's nitrogen one, both SCARCE: nitrogen first, though hydrogen pays more. Of the MODERATE pairs, C39's
        // hydrocarbon, which pays most, has two.
        var siphoners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [MiningPlanner.OpportunityKey(G50, "LIQUID_HYDROGEN")] = 2,
            [MiningPlanner.OpportunityKey(E47, "LIQUID_NITROGEN")] = 1,
            [MiningPlanner.OpportunityKey(G50, "HYDROCARBON")] = 1,
            [MiningPlanner.OpportunityKey(C39, "HYDROCARBON")] = 2,
            [MiningPlanner.OpportunityKey(E47, "LIQUID_HYDROGEN")] = 1,
            [MiningPlanner.OpportunityKey(G50, "LIQUID_NITROGEN")] = 1,
            [MiningPlanner.OpportunityKey(C39, "LIQUID_HYDROGEN")] = 1,
            [MiningPlanner.OpportunityKey(C39, "LIQUID_NITROGEN")] = 1,
        };

        var targets = SiphonPlanner.SharedTargets(Map(), SiphonDrone(), siphoners);

        targets.Select(target => (target.Gas, target.SellWaypointSymbol)).Should().Equal(
            ("LIQUID_NITROGEN", E47),
            ("LIQUID_HYDROGEN", G50),
            ("HYDROCARBON", G50),
            ("LIQUID_HYDROGEN", E47),
            ("LIQUID_NITROGEN", G50),
            ("LIQUID_HYDROGEN", C39),
            ("LIQUID_NITROGEN", C39),
            ("HYDROCARBON", C39));
    }

    [Fact]
    public void ASiphonDroneShares_NoPairAtAbundant()
    {
        // D77: sharing is for the pairs below ABUNDANT; C39 has all the hydrocarbon it wants.
        var siphoners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [MiningPlanner.OpportunityKey(C39, "HYDROCARBON")] = 1 };

        var targets = SiphonPlanner.SharedTargets(Map(MarketsWithHydrocarbonAtC39("ABUNDANT")), SiphonDrone(), siphoners);

        targets.Should().NotContain(target => target.Gas == "HYDROCARBON" && target.SellWaypointSymbol == C39);
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

        var targets = SiphonPlanner.SiphonTargets(Map(markets), SiphonDrone(), held, [Covering("HYDROCARBON", G50)]);

        targets.Select(target => (target.Gas, target.SellWaypointSymbol)).Take(3).Should().Equal(
            ("LIQUID_HYDROGEN", G50),
            ("LIQUID_NITROGEN", E47),
            ("HYDROCARBON", C39));
    }

    [Fact]
    public void TheScarceGases_AreThoseADroneCouldServeAMarketShortOf()
    {
        SiphonPlanner.ScarceGases(Map(), SiphonDrone()).Select(area => (area.Good, string.Join(',', area.MarketSymbols))).Should().Equal(
            ("HYDROCARBON", G50),
            ("LIQUID_HYDROGEN", G50),
            ("LIQUID_NITROGEN", E47));
    }

    [Fact]
    public void TheScarceGases_CountOncePerArea()
    {
        // D53: with D90 near F48, F48's nitrogen and hydrogen count too, a drift away (D45). F48 is beyond a drone's tank from
        // G50 and E47: another area, so each of its gases counts again.
        SiphonPlanner.ScarceGases(MapWithAGasGiantNearF48(), SiphonDrone()).Select(area => (area.Good, string.Join(',', area.MarketSymbols))).Should().Equal(
            ("HYDROCARBON", G50),
            ("LIQUID_HYDROGEN", F48),
            ("LIQUID_HYDROGEN", G50),
            ("LIQUID_NITROGEN", E47),
            ("LIQUID_NITROGEN", F48));
    }

    [Fact]
    public void AGasASiphonerWorksOnForAFarMarket_IsStillUncoveredInReach()
    {
        // D53: "A drone covers a mineral only for the markets it can reach in CRUISE from where it works." A drone siphons
        // nitrogen for F48, beyond a drone's tank from E47, so E47's SCARCE nitrogen still has nobody, and comes before G50's
        // LIMITED hydrocarbon.
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(G50, "LIQUID_HYDROGEN"), MiningPlanner.OpportunityKey(F48, "LIQUID_NITROGEN") };

        var targets = SiphonPlanner.SiphonTargets(
            MapWithAGasGiantNearF48(),
            SiphonDrone(),
            held,
            [Covering("LIQUID_HYDROGEN", G50), Covering("LIQUID_NITROGEN", F48)]);

        targets.Select(target => (target.Gas, target.SellWaypointSymbol)).Take(3).Should().Equal(
            ("LIQUID_NITROGEN", E47),
            ("HYDROCARBON", G50),
            ("LIQUID_HYDROGEN", F48));
    }

    [Fact]
    public void AFarMarketWithoutAGasGiantWithinARoundTripOfIt_IsNoTarget()
    {
        // D45: F48 is beyond a drone's tank and sells fuel, but C38, the one gas giant, is 228 from it: X1-DC53's siphon
        // drones have no reason to drift.
        SiphonPlanner.SiphonTargets(Map(), SiphonDrone(), new HashSet<string>()).Should().NotContain(target => target.SellWaypointSymbol == F48);
    }

    [Fact]
    public void AMarketBeyondADronesTank_IsAFarTarget_WithAGasGiantWithinARoundTripOfIt()
    {
        // D45, "a way to add mining/siphoning drones for the minerals outside of fuel range": with D90 13 from F48, F48's
        // gases count, a drift away. A far target ranks after the reachable ones of its supply level: F48's SCARCE
        // nitrogen after G50's and E47's SCARCE gases, though it pays most; its LIMITED hydrogen after G50's hydrocarbon.
        var targets = SiphonPlanner.SiphonTargets(MapWithAGasGiantNearF48(), SiphonDrone(), new HashSet<string>());

        targets.Select(target => (target.Gas, target.SellWaypointSymbol, target.Far)).Take(5).Should().Equal(
            ("LIQUID_HYDROGEN", G50, false),
            ("LIQUID_NITROGEN", E47, false),
            ("LIQUID_NITROGEN", F48, true),
            ("HYDROCARBON", G50, false),
            ("LIQUID_HYDROGEN", F48, true));
        targets.Where(target => target.Far).Should().OnlyContain(target => target.GasGiantSymbol == D90);
        targets.Where(target => !target.Far).Should().OnlyContain(target => target.GasGiantSymbol == C38);
    }

    [Fact]
    public void AScarceGasNoSiphonerWorksOn_ComesFirst_ThoughADriftAway()
    {
        // D48 with D45: siphoners work on hydrogen and hydrocarbon, and only F48, a drift away, is short of nitrogen.
        MarketSnapshot[] markets =
        [
            .. Markets().Where(market => market.WaypointSymbol != E47),
            Market(
                E47,
                Good("LIQUID_NITROGEN", "IMPORT", 100, 50, 60, "MODERATE"),
                Good("LIQUID_HYDROGEN", "IMPORT", 96, 48, 60, "MODERATE"),
                Good("FUEL", "EXCHANGE", 81, 71, 180, "MODERATE")),
        ];

        var targets = SiphonPlanner.SiphonTargets(
            MapWithAGasGiantNearF48(markets),
            SiphonDrone(),
            new HashSet<string>(),
            [Covering("HYDROCARBON", G50), Covering("LIQUID_HYDROGEN", G50)]);

        (targets[0].Gas, targets[0].SellWaypointSymbol, targets[0].Far).Should().Be(("LIQUID_NITROGEN", F48, true));
    }

    [Fact]
    public void OfTheGasesNoSiphonerWorksOn_ThoseInReach_ComeBeforeThoseADriftAway()
    {
        // D48's "near before far", and D45's far targets after the reachable ones.
        var targets = SiphonPlanner.SiphonTargets(MapWithAGasGiantNearF48(), SiphonDrone(), new HashSet<string>(), Array.Empty<CoveringTrip>());

        targets.Select(target => (target.Gas, target.SellWaypointSymbol)).Take(5).Should().Equal(
            ("LIQUID_HYDROGEN", G50),
            ("LIQUID_NITROGEN", E47),
            ("HYDROCARBON", G50),
            ("LIQUID_NITROGEN", F48),
            ("LIQUID_HYDROGEN", F48));
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

    /// <summary>A siphoner's trip on a gas for a market, by a drone with an 80-unit tank.</summary>
    private static CoveringTrip Covering(string gas, string market) => new(gas, market, 80);

    /// <summary>The fixture's markets, with C39's hydrocarbon at the supply given: MODERATE in the fixture.</summary>
    private static MarketSnapshot[] MarketsWithHydrocarbonAtC39(string supply)
        =>
        [
            .. Markets().Where(market => market.WaypointSymbol != C39),
            Market(
                C39,
                Good("HYDROCARBON", "EXCHANGE", 70, 60, 60, supply),
                Good("LIQUID_HYDROGEN", "EXCHANGE", 40, 35, 60, "MODERATE"),
                Good("LIQUID_NITROGEN", "EXCHANGE", 34, 30, 60, "MODERATE"),
                Good("FUEL", "EXCHANGE", 80, 70, 180, "MODERATE")),
        ];
}

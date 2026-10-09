using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.4: what a surveyor surveys (the contract's ore first, then sellable ores near the market that
/// buys them, each until it has a stock of usable surveys, D27) and what a miner mines (the markets shortest
/// of an ore first, D28; within a supply level, surveyed ores first). D77: no market that has the ore ABUNDANT, and a drone
/// shares a pair once every pair below ABUNDANT has a miner.
/// </summary>
public sealed class MiningPlannerTests
{
    [Fact]
    public void TheContractsOre_IsSurveyedFirst_UntilItHasItsStock()
    {
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE"));

        var targets = MiningPlanner.SurveyTargets(context, [new ContractOre("COPPER_ORE", XB5C, H51)], [Drone()], stock: 2);

        targets[0].Should().BeEquivalentTo(new SurveyTarget("COPPER_ORE", XB5C, H51, 0, ForContract: true, UsableSurveys: 1, NeedsSurvey: true));
    }

    [Fact]
    public void OnceTheContractsOreHasItsStock_TheOresStillShortOfSurveysComeFirst()
    {
        // D27, seen on the cluster on 2026-10-02: the contract's ore came first whatever surveys there
        // were, so SPECTER-1 surveyed XB5C "for copper" 36 times in half an hour, and 27 surveys lay unused.
        // "I'd expect him to make 1 copper ore survey and then move to the next ore type", with a stock of
        // two usable surveys per ore.
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE"), Survey("S-2", XB5C, "COPPER_ORE", "IRON_ORE"));

        var targets = MiningPlanner.SurveyTargets(context, [new ContractOre("COPPER_ORE", XB5C, H51)], [Drone()], stock: 2);

        targets.Where(target => target.NeedsSurvey).Select(target => target.Ore)
            .Should().Equal("ALUMINUM_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND", "IRON_ORE");
        targets.Single(target => target.ForContract).NeedsSurvey.Should().BeFalse();
    }

    [Fact]
    public void EveryMarketThatBuysAnOre_GetsTheAsteroidNearestIt()
    {
        // D27, refined on 2026-10-02: "Stock per ore per asteroid. I'd like the surveys to be close to
        // wherever the mineral can be sold." COPPER sells at H51 and at B7: near H51 that is XB5C, near B7
        // it is B14, for a miner that can get there.
        var targets = MiningPlanner.SurveyTargets(Context(), [], [CommandShip()], stock: 2);

        targets.Where(target => target.Ore == "COPPER_ORE").Select(target => (target.AsteroidSymbol, target.BuyerSymbol))
            .Should().BeEquivalentTo([(XB5C, H51), (B14, B7)]);
    }

    [Fact]
    public void TheStock_IsKeptPerAsteroid()
    {
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE"), Survey("S-2", XB5C, "COPPER_ORE"));

        var targets = MiningPlanner.SurveyTargets(context, [], [CommandShip()], stock: 2);

        targets.Single(target => target.Ore == "COPPER_ORE" && target.AsteroidSymbol == XB5C).NeedsSurvey.Should().BeFalse();
        targets.Single(target => target.Ore == "COPPER_ORE" && target.AsteroidSymbol == B14).NeedsSurvey.Should().BeTrue();
    }

    [Fact]
    public void OnceEveryOreHasItsStock_NothingNeedsASurvey()
    {
        var context = Context(
            Survey("S-1", XB5C, "COPPER_ORE", "ALUMINUM_ORE", "IRON_ORE"),
            Survey("S-2", XB5C, "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE"),
            Survey("S-3", XB5C, "ALUMINUM_ORE", "IRON_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND"));

        var targets = MiningPlanner.SurveyTargets(context, [new ContractOre("COPPER_ORE", XB5C, H51)], [Drone()], stock: 2);

        targets.Should().HaveCount(6).And.OnlyContain(target => !target.NeedsSurvey);
    }

    [Fact]
    public void WithoutAContract_SellableOres_AreSurveyedAtTheAsteroidNearestTheirBuyer_ThatTheMinersCanReach()
    {
        // GOLD pays best, at B7, but the drone can't reach B14 next to it.
        var targets = MiningPlanner.SurveyTargets(Context(), [], [Drone()], stock: 2);

        targets.Select(target => (target.Ore, target.AsteroidSymbol, target.BuyerSymbol)).Should().Equal(
            ("COPPER_ORE", XB5C, H51),
            ("ALUMINUM_ORE", XB5C, H51),
            ("IRON_ORE", XB5C, H51),
            ("SILICON_CRYSTALS", XB5C, F49),
            ("QUARTZ_SAND", XB5C, F49));
    }

    [Fact]
    public void OresWithFewerUsableSurveys_AreSurveyedFirst()
    {
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE", "IRON_ORE"));

        var targets = MiningPlanner.SurveyTargets(context, [], [Drone()], stock: 2);

        targets.Select(target => target.Ore).Should().Equal("ALUMINUM_ORE", "SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE", "IRON_ORE");
    }

    [Fact]
    public void AnAsteroidIsSurveyed_OnlyWhereAMinerCouldMineForTheMarket()
    {
        // B54, seen on the cluster on 2026-10-03 at the first drift (slice 6.10c): with a drone bound for B7, the survey plan
        // sent the command ship to survey B37 for B7's gold, 68 from B7, 136 there and back: more than a drone's tank, so no
        // drone mines there. A survey is for a miner's trip: to the asteroid and on to the market, in CRUISE, with the fuel
        // left. Here B7 buys iron too, which near B7 only B13, 48 away, yields: a drone at B7 reaches it, but can't bring
        // the iron back.
        var targets = MiningPlanner.SurveyTargets(new MiningContext(MapWithIronAtB7(), [], 129_357, Now), [], [Drone(waypoint: B7)], stock: 2);

        targets.Should().Contain(target => target.Ore == "GOLD_ORE" && target.AsteroidSymbol == B14 && target.BuyerSymbol == B7);
        targets.Should().NotContain(target => target.Ore == "IRON_ORE" && target.BuyerSymbol == B7);
    }

    [Fact]
    public void WithoutMiners_EveryAsteroidCounts()
    {
        var targets = MiningPlanner.SurveyTargets(Context(), [], [], stock: 2);

        targets[0].Should().BeEquivalentTo(new SurveyTarget("GOLD_ORE", B14, B7, 114, ForContract: false, UsableSurveys: 0, NeedsSurvey: true));
    }

    [Fact]
    public void WithinASupplyLevel_ASurveyedOreComesFirst_TheOneASingleExtractionFetchesMostFor()
    {
        // Copper is two of three deposits: 2/3 x 67 beats iron's 1/3 x 58, both LIMITED at H51.
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE", "COPPER_ORE", "IRON_ORE"));

        var targets = MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>());

        var limited = targets.Where(target => target.Supply == "LIMITED").ToList();
        limited[0].Should().BeEquivalentTo(new MiningTarget("COPPER_ORE", XB5C, H51, 67, 2 / 3.0, Surveyed: true, Supply: "LIMITED"));
        limited[1].Ore.Should().Be("IRON_ORE");
        limited[1].Surveyed.Should().BeTrue();
        targets.Where(target => target.Supply == "SCARCE").Should().OnlyContain(target => !target.Surveyed);
    }

    [Fact]
    public void AMiner_ServesTheMarketsShortestOfAnOreFirst_SurveyedOrNot()
    {
        // D28, seen on the cluster on 2026-10-02: the mining plan bought SPECTER-4 for A3's scarce silicon, and
        // the drone mined surveyed iron for H51, where iron was MODERATE, so the opening that paid for it stayed
        // open, ready to pay for the next drone. "Mine for scarce first, but once all ores are no longer SCARCE,
        // keep mining for whatever the lowest supply ore is, even if it's not that profitable." A survey that is
        // mostly aluminum doesn't put H51's aluminum (MODERATE) before the scarce and limited markets. B7's scarce
        // markets are a drift away (D45): after F49's, before H51's.
        var context = Context(Survey("S-1", XB5C, "ALUMINUM_ORE", "ALUMINUM_ORE", "ALUMINUM_ORE", "COPPER_ORE"));

        var targets = MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>());

        targets.Select(target => (target.Ore, target.SellWaypointSymbol, target.Supply)).Should().Equal(
            ("SILICON_CRYSTALS", F49, "SCARCE"),
            ("QUARTZ_SAND", F49, "SCARCE"),
            ("GOLD_ORE", B7, "SCARCE"),
            ("COPPER_ORE", B7, "SCARCE"),
            ("COPPER_ORE", H51, "LIMITED"),
            ("IRON_ORE", H51, "LIMITED"),
            ("ALUMINUM_ORE", H51, "MODERATE"));
        targets.Select(target => target.LowSupply).Should().Equal(true, true, true, true, true, true, false);
    }

    [Fact]
    public void WithoutSurveys_AMiner_MinesAtTheAsteroidNearestEachMarket_TheLowestSupplyFirst_AndSellsItThere()
    {
        // F49 is SCARCE of silicon and quartz, H51 LIMITED of copper and iron and MODERATE of aluminum (D28).
        // B7's markets, SCARCE of gold and copper, are beyond a drone's tank: it would drift there (D45), so they
        // come after F49's, and B14, next to B7, yields both.
        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>());

        targets.Select(target => (target.Ore, target.AsteroidSymbol, target.SellWaypointSymbol)).Should().Equal(
            ("SILICON_CRYSTALS", XB5C, F49),
            ("QUARTZ_SAND", XB5C, F49),
            ("GOLD_ORE", B14, B7),
            ("COPPER_ORE", B14, B7),
            ("COPPER_ORE", XB5C, H51),
            ("IRON_ORE", XB5C, H51),
            ("ALUMINUM_ORE", XB5C, H51));
        targets[0].Share.Should().BeApproximately(1 / 6.0, 1e-9, "XB5C yields six ores");
    }

    [Fact]
    public void AMarketBeyondADronesTank_IsAFarTarget_ThatRanksAfterTheReachableOnesOfItsSupplyLevel()
    {
        // D45, asked on 2026-10-03: "I'd like a way to add mining/siphoning drones for the minerals outside of fuel range,
        // e.g. by having a drone drift to the marketplace that buys the mineral first, then refueling and resuming normal
        // behavior." No chain of fuel markets 80 apart leads from H51 to B7, which sells fuel, and B14 is 25 from it: there
        // and back on one tank. A far target ranks after any reachable one of the same supply level, so B7's SCARCE gold
        // comes after F49's SCARCE silicon and quartz, and before H51's LIMITED copper, though it pays most.
        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>());

        targets.Select(target => target.Far).Should().Equal(false, false, true, true, false, false, false);
        targets.Single(target => target.Ore == "GOLD_ORE").Should().BeEquivalentTo(
            new MiningTarget("GOLD_ORE", B14, B7, 114, 1 / 8.0, Surveyed: false, Supply: "SCARCE", Far: true));
    }

    [Fact]
    public void AFarMarket_CountsOnlyAnAsteroidWithinACruiseRoundTripOfIt()
    {
        // D45: "Only targets whose asteroid is within a CRUISE round trip of that market." Here B7 buys iron too, but the
        // asteroid nearest it that yields iron, B13, is 48 away: there and back is 96, more than a drone's 80-unit tank.
        var targets = MiningPlanner.MiningTargets(new MiningContext(MapWithIronAtB7(), [], 129_357, Now), Drone(), new HashSet<string>());

        targets.Should().Contain(target => target.Ore == "GOLD_ORE" && target.SellWaypointSymbol == B7 && target.Far);
        targets.Should().NotContain(target => target.Ore == "IRON_ORE" && target.SellWaypointSymbol == B7);
    }

    [Fact]
    public void AFarMarketThatSellsNoFuel_IsNoTarget()
    {
        // The drone fills its tank at the market it drifts to, to mine from there in CRUISE (D45).
        var map = Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != B7),
            Market(B7, Good("GOLD_ORE", "IMPORT", 230, 114, 60, "SCARCE"), Good("COPPER_ORE", "IMPORT", 68, 58, 180, "SCARCE")),
        ]);

        var targets = MiningPlanner.MiningTargets(new MiningContext(map, [], 129_357, Now), Drone(), new HashSet<string>());

        targets.Should().NotContain(target => target.SellWaypointSymbol == B7);
    }

    [Fact]
    public void AtTheFarMarket_ADroneMinesFromThereInCruise_AndTheMiddleIsADriftAway()
    {
        // Once it has drifted to B7 and docked there (D45), B7's gold and copper are in reach: B14 and back on one tank.
        var targets = MiningPlanner.MiningTargets(Context(), Drone(waypoint: B7), new HashSet<string>());

        targets.Select(target => (target.Ore, target.SellWaypointSymbol, target.Far)).Should().Equal(
            ("GOLD_ORE", B7, false),
            ("COPPER_ORE", B7, false),
            ("SILICON_CRYSTALS", F49, true),
            ("QUARTZ_SAND", F49, true),
            ("COPPER_ORE", H51, true),
            ("IRON_ORE", H51, true),
            ("ALUMINUM_ORE", H51, true));
    }

    [Fact]
    public void FromItsMarket_ADroneMinesOnlyWhereItGetsBackInCruise()
    {
        // D45: the drone "mines from that market in CRUISE". Leaving B7 with a full tank it would get to B13, 48 away, with
        // 32 left: too little to carry iron back, and the navigation would drift back (B47). A trip counts the fuel a ship
        // has where it fills its hold; only a source that sells fuel, as XB5C does, fills its tank again.
        var targets = MiningPlanner.MiningTargets(new MiningContext(MapWithIronAtB7(), [], 129_357, Now), Drone(waypoint: B7), new HashSet<string>());

        targets.Should().NotContain(target => target.Ore == "IRON_ORE" && target.SellWaypointSymbol == B7);
        targets.Should().Contain(target => target.Ore == "GOLD_ORE" && target.SellWaypointSymbol == B7 && !target.Far);
    }

    [Fact]
    public void AShipTakesAnOpening_ItReachesOrCouldDriftTo()
    {
        // The openings the mining plan lists, with the miners that could take them (D13 reads them): B7's gold from B14
        // takes a drift (D45); B7's gold from B13 is beyond a round trip of B7; F49's silicon from XB5C is in reach.
        MiningPlanner.CanTake(Map(), Drone(), B14, B7).Should().BeTrue();
        MiningPlanner.CanTake(Map(), Drone(), B13, B7).Should().BeFalse();
        MiningPlanner.CanTake(Map(), Drone(), XB5C, F49).Should().BeTrue();
    }

    [Fact]
    public void AnOpeningAnotherMinerHolds_IsNotOffered()
    {
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(F49, "SILICON_CRYSTALS") };

        var targets = MiningPlanner.MiningTargets(Context(), Drone(), held);

        targets.Should().NotContain(target => target.Key == MiningPlanner.OpportunityKey(F49, "SILICON_CRYSTALS"));
        targets[0].Ore.Should().Be("QUARTZ_SAND");
    }

    [Fact]
    public void ALongRangeShip_ReachesTheFarOpenings()
    {
        var targets = MiningPlanner.MiningTargets(Context(), CommandShip(), new HashSet<string>());

        targets.Should().Contain(target => target.Ore == "GOLD_ORE" && target.AsteroidSymbol == B14 && target.SellWaypointSymbol == B7);
        targets.Should().Contain(target => target.Ore == "COPPER_ORE" && target.AsteroidSymbol == B14 && target.SellWaypointSymbol == B7, "B14 yields copper too, and is nearer B7 than B13");
    }

    [Theory]
    [InlineData("HIGH", true)]
    [InlineData("ABUNDANT", false)]
    public void AMarketThatHasAllTheOreItWants_IsNoMiningTarget(string supply, bool isTarget)
    {
        // D77, asked on 2026-10-05: "They can mine until every mineral is ABUNDANT." At ABUNDANT, H51 has all the aluminum it
        // wants, and no miner mines it for H51; at HIGH it is still the lowest supply left to mine for (D28).
        var targets = MiningPlanner.MiningTargets(new MiningContext(MapWithAluminumAtH51(supply), [], 129_357, Now), Drone(), new HashSet<string>());

        targets.Any(target => target.Ore == "ALUMINUM_ORE").Should().Be(isTarget);
        targets.Should().Contain(target => target.Ore == "COPPER_ORE" && target.SellWaypointSymbol == H51, "H51's other ores still count");
    }

    [Fact]
    public void ADroneThatShares_TakesTheLowestSupply_InReachFirst_ThenThePairWithTheFewestMiners()
    {
        // D77, asked on 2026-10-05: once every pair below ABUNDANT has a miner, a drone shares one rather than trade. F49's
        // silicon has two drones and its quartz one, both SCARCE and in reach: quartz first, though silicon pays more. B7's
        // SCARCE gold and copper are a drift away, so they come after F49's, whatever their drones (D45). At H51, LIMITED,
        // copper has two drones and iron one; MODERATE aluminum comes last.
        var miners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [MiningPlanner.OpportunityKey(F49, "SILICON_CRYSTALS")] = 2,
            [MiningPlanner.OpportunityKey(F49, "QUARTZ_SAND")] = 1,
            [MiningPlanner.OpportunityKey(B7, "GOLD_ORE")] = 1,
            [MiningPlanner.OpportunityKey(B7, "COPPER_ORE")] = 1,
            [MiningPlanner.OpportunityKey(H51, "COPPER_ORE")] = 2,
            [MiningPlanner.OpportunityKey(H51, "IRON_ORE")] = 1,
            [MiningPlanner.OpportunityKey(H51, "ALUMINUM_ORE")] = 1,
        };

        var targets = MiningPlanner.SharedTargets(Context(), Drone(), miners);

        targets.Select(target => (target.Ore, target.SellWaypointSymbol)).Should().Equal(
            ("QUARTZ_SAND", F49),
            ("SILICON_CRYSTALS", F49),
            ("GOLD_ORE", B7),
            ("COPPER_ORE", B7),
            ("IRON_ORE", H51),
            ("COPPER_ORE", H51),
            ("ALUMINUM_ORE", H51));
    }

    [Fact]
    public void ADroneShares_NoPairAtAbundant()
    {
        // D77: sharing is for the pairs below ABUNDANT; H51 has all the aluminum it wants.
        var miners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [MiningPlanner.OpportunityKey(H51, "ALUMINUM_ORE")] = 1 };

        var targets = MiningPlanner.SharedTargets(new MiningContext(MapWithAluminumAtH51("ABUNDANT"), [], 129_357, Now), Drone(), miners);

        targets.Should().NotContain(target => target.Ore == "ALUMINUM_ORE");
    }

    [Fact]
    public void AMarketWhosePricesAreTooOld_IsNoTarget_NorAnOpening()
    {
        // Slice 6.40 (D122): abroad only the markets the probes keep fresh count, as for a trade route (D96). F49 was last seen
        // too long ago: its silicon and quartz are neither mined for nor counted as short.
        var context = new MiningContext(Map().WithStaleMarkets(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { F49 }), [], 129_357, Now);

        MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>()).Should().NotContain(target => target.SellWaypointSymbol == F49);
        MiningPlanner.ScarceOres(context, Drone()).Select(area => area.Good).Should().NotContain(["QUARTZ_SAND", "SILICON_CRYSTALS"]);
        MiningPlanner.LowSupplyOpportunities(context.Map).Should().NotContain(opening => opening.SellWaypointSymbol == F49);
        MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>()).Should().Contain(target => target.SellWaypointSymbol == F49, "at home every market counts");
    }

    [Fact]
    public void APairLeftToTheTrade_IsNoTarget_ButTheMarketsOtherOresAre()
    {
        // Slice 6.40 (D122): abroad a trade trip on its way to sell F49 silicon keeps the miners off that pair, shared or not.
        var context = Context() with { LeftToTrade = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MiningPlanner.OpportunityKey(F49, "SILICON_CRYSTALS") } };

        MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>())
            .Where(target => target.SellWaypointSymbol == F49).Select(target => target.Ore).Distinct().Should().Equal("QUARTZ_SAND");
        MiningPlanner.SharedTargets(context, Drone(), new Dictionary<string, int>()).Should().NotContain(target => target.Ore == "SILICON_CRYSTALS");
        MiningPlanner.ScarceOres(context, Drone()).Select(area => area.Good).Should().NotContain("SILICON_CRYSTALS");
    }

    [Fact]
    public void TheLowSupplyOpenings_AreEveryMarketWithAnOreInLowSupply_WithTheAsteroidNearestIt()
    {
        var openings = MiningPlanner.LowSupplyOpportunities(Map());

        openings.Select(opening => (opening.SellWaypointSymbol, opening.Ore, opening.AsteroidSymbol)).Should().BeEquivalentTo(
        [
            (B7, "COPPER_ORE", B14),
            (B7, "GOLD_ORE", B14),
            (F49, "QUARTZ_SAND", XB5C),
            (F49, "SILICON_CRYSTALS", XB5C),
            (H51, "COPPER_ORE", XB5C),
            (H51, "IRON_ORE", XB5C),
        ]);
    }

    [Fact]
    public void AScarceOreNoMinerWorksOn_ComesFirst_ThoughAnOreAMinerWorksOnIsShorter()
    {
        // Slice 6.10b (D48): H52 is SCARCE of silicon too, but a miner works on silicon for F49. Quartz, copper and iron
        // have nobody; aluminum is MODERATE, which no drone is kept for.
        var map = Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != H52),
            Market(H52, Good("SILICON_CRYSTALS", "IMPORT", 100, 50, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 76, 69, 180, "MODERATE")),
        ]);
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(F49, "SILICON_CRYSTALS") };

        var targets = MiningPlanner.MiningTargets(new MiningContext(map, [], 129_357, Now), Drone(), held, [Covering("SILICON_CRYSTALS", F49)]);

        // B7's gold and copper have nobody either, but are a drift away (D45): after the reachable ones.
        targets.Select(target => (target.Ore, target.SellWaypointSymbol)).Should().Equal(
            ("QUARTZ_SAND", F49),
            ("COPPER_ORE", H51),
            ("IRON_ORE", H51),
            ("GOLD_ORE", B7),
            ("COPPER_ORE", B7),
            ("SILICON_CRYSTALS", H52),
            ("ALUMINUM_ORE", H51));
    }

    [Fact]
    public void AScarceOreNoMinerWorksOn_ComesFirst_ThoughADriftAway()
    {
        // D48 with D45: every ore near the middle has a drone, and nobody mines gold. A free drone takes B7's gold, a drift
        // away, before the ores the others work on: "at least 1 drone per mineral that is scarce or limited".
        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>(), MiddleCovered());

        targets[0].Should().BeEquivalentTo(new MiningTarget("GOLD_ORE", B14, B7, 114, 1 / 8.0, Surveyed: false, Supply: "SCARCE", Far: true));
    }

    [Fact]
    public void OfTheScarceOresNoMinerWorksOn_TheNearestAsteroidComesFirst()
    {
        // D48, "near before far": an asteroid with mineral deposits 5 from H51 yields iron, which D28 alone would put after
        // copper, as copper fetches more; copper comes from XB5C, 19 away.
        var nearby = new WaypointCacheModel("X1-DC53-XN1", SystemSymbol, "ASTEROID", -18, 45, false, false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"MINERAL_DEPOSITS"}]""");
        var map = new TradeMarketMap([.. Waypoints, nearby], Markets(), new Dictionary<string, IReadOnlyList<string>>());

        var targets = MiningPlanner.MiningTargets(
            new MiningContext(map, [], 129_357, Now),
            Drone(),
            new HashSet<string>(),
            [Covering("SILICON_CRYSTALS", F49), Covering("QUARTZ_SAND", F49)]);

        targets.Select(target => (target.Ore, target.AsteroidSymbol)).Take(2).Should().Equal(("IRON_ORE", "X1-DC53-XN1"), ("COPPER_ORE", XB5C));
    }

    [Fact]
    public void TheScarceOres_AreThoseADroneCouldServeAMarketShortOf_WhoeverWorksOnThem_OncePerArea()
    {
        // B7's gold and copper are beyond a drone's tank, but a drift away (D45), so they count for it too. D53: copper is
        // short at H51 in the middle and at B7, which a drone doesn't fly between in CRUISE: two areas, so it counts twice.
        MiningPlanner.ScarceOres(Context(), Drone()).Select(area => (area.Good, string.Join(',', area.MarketSymbols))).Should().Equal(
            ("COPPER_ORE", B7),
            ("COPPER_ORE", H51),
            ("GOLD_ORE", B7),
            ("IRON_ORE", H51),
            ("QUARTZ_SAND", F49),
            ("SILICON_CRYSTALS", F49));
    }

    [Fact]
    public void MarketsADroneFliesBetweenInCruise_ShareAnArea()
    {
        // D53: H52 is SCARCE of silicon too, 39 from F49: one area with it. The command ship's 400-unit tank flies from H51 to
        // B7 in CRUISE, so for it copper has a single area.
        var map = Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != H52),
            Market(H52, Good("SILICON_CRYSTALS", "IMPORT", 100, 50, 60, "SCARCE"), Good("FUEL", "EXCHANGE", 76, 69, 180, "MODERATE")),
        ]);

        MiningPlanner.ScarceOres(new MiningContext(map, [], 129_357, Now), Drone())
            .Single(area => area.Good == "SILICON_CRYSTALS").MarketSymbols.Should().Equal(F49, H52);
        MiningPlanner.ScarceOres(Context(), CommandShip())
            .Single(area => area.Good == "COPPER_ORE").MarketSymbols.Should().Equal(B7, H51);
    }

    [Fact]
    public void AnOreADroneMinesForAFarMarket_IsStillUncoveredInTheMiddle()
    {
        // D53, seen on the cluster on 2026-10-03: with SPECTER-10 bound for B7's silicon, silicon counted as covered, and the
        // middle's SCARCE silicon had no drone while four of five mining drones went to B7. "A drone covers a mineral only
        // for the markets it can reach in CRUISE from where it works (the middle, or B7)." Here a drone mines copper for B7,
        // and H51's LIMITED copper, in reach, comes before B7's gold, a drift away.
        var covering = new[] { Covering("SILICON_CRYSTALS", F49), Covering("QUARTZ_SAND", F49), Covering("IRON_ORE", H51), Covering("COPPER_ORE", B7) };

        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>(), covering);

        (targets[0].Ore, targets[0].SellWaypointSymbol, targets[0].Far).Should().Be(("COPPER_ORE", H51, false));
    }

    [Fact]
    public void ATripCoversItsOre_AtTheMarketsItsShipReachesInCruise_FromWhereItSells()
    {
        // D53: a drone that sells at B7 covers B7, not H51; the command ship's 400-unit tank reaches H51 from B7.
        Covering("COPPER_ORE", B7).Covers(Map(), "COPPER_ORE", B7).Should().BeTrue();
        Covering("COPPER_ORE", B7).Covers(Map(), "COPPER_ORE", H51).Should().BeFalse();
        Covering("COPPER_ORE", B7, tank: 400).Covers(Map(), "COPPER_ORE", H51).Should().BeTrue();
        Covering("COPPER_ORE", H51).Covers(Map(), "IRON_ORE", H51).Should().BeFalse();
    }

    [Fact]
    public void ADrone_CantReachTheFarAsteroids_InCruise()
    {
        MiningPlanner.CanReach(Map(), Drone(), XB5C).Should().BeTrue();
        MiningPlanner.CanReach(Map(), Drone(), B14).Should().BeFalse();
        MiningPlanner.CanReach(Map(), CommandShip(), B14).Should().BeTrue();
    }

    [Fact]
    public void ASurveyShip_MovesToTheAreaWhereMoreDronesMine()
    {
        // D54, asked on 2026-10-03: "Please add the option for the survey ship to get to the mining location without
        // surveys", to work where most drones mine. Two drones work for B7, beyond an 80-unit tank's CRUISE reach, and one
        // for H51: the survey ship drifts to B7, which sells fuel (D45).
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(), [B7, H51, B7], [], out var move).Should().BeTrue();

        move.Should().Be(new SurveyorMove(B7, Drones: 2, OwnDrones: 1));
    }

    [Fact]
    public void OnATie_TheSurveyShipStaysWhereItIs()
    {
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(), [B7, H51], [], out _).Should().BeFalse();
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(waypoint: B7), [B7, H51], [], out _).Should().BeFalse();
    }

    [Fact]
    public void WhereMostDronesMine_TheSurveyShipStays_AndFromThereItWouldMoveBackWhenThatChanges()
    {
        // At B7, its area is B7's. With more drones in the middle it goes back, to the market there where the most of them
        // work: two for F49, one for H51.
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(waypoint: B7), [B7, B7, H51], [], out _).Should().BeFalse();
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(waypoint: B7), [B7, H51, F49, F49], [], out var move).Should().BeTrue();

        move.Should().Be(new SurveyorMove(F49, Drones: 3, OwnDrones: 1));
    }

    [Fact]
    public void AnAreaWithoutAMarketThatSellsFuel_IsNoPlaceToMoveTo()
    {
        // Drones between trips at B14 count where they are; the survey ship drifts only to a market that sells fuel (D45), and
        // none of those waypoints is one.
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(), [B14, B14, H51], [], out _).Should().BeFalse();
    }

    [Fact]
    public void ASurveyShipThatSharesItsArea_DriftsToAnAreaWithDronesThatHasNone()
    {
        // D55, asked on 2026-10-03: "extra surveyor drones are bought to try and cover all areas with surveys". Two survey
        // ships work in the middle, where three drones mine; B7's one drone has none: one of them goes there.
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(), [H51, F49, F49, B7], [XB5C], out var move).Should().BeTrue();

        move.Should().Be(new SurveyorMove(B7, Drones: 1, OwnDrones: 3, Shared: true));
    }

    [Fact]
    public void AnAreaAnotherSurveyShipWorksIn_IsNoPlaceToMoveTo()
    {
        // D55: each area with drones has a survey ship of its own. B7 has more drones, but a survey ship already, at B7 or
        // on its way there.
        MiningPlanner.TryFindBusierArea(Map(), SurveyShip(), [H51, B7, B7], [B7], out _).Should().BeFalse();
    }

    [Fact]
    public void TheAreasWithDrones_AreCountedAsAShipFliesBetweenThem()
    {
        // D55: a survey ship's 80-unit tank keeps the middle and B7 apart; the command ship's 400 doesn't.
        MiningPlanner.CountAreas(Map(), 80, [H51, F49, B7, B7]).Should().Be(2);
        MiningPlanner.CountAreas(Map(), 400, [H51, F49, B7, B7]).Should().Be(1);
        MiningPlanner.CountAreas(Map(), 80, []).Should().Be(0);
    }

    [Fact]
    public void AMarketThatMakesSomethingFromTheOre_ComesFirst_ThoughOnesThatOnlyPayForItAreShorterAndPayMore()
    {
        // D91, asked on 2026-10-05: "first redirect the ore to a place that actually generates iron", and "EXCHANGE nodes
        // should be lowest priority and only considered as wealth trades, never as supply trades." F49 and XB5C are SCARCE
        // and pay more, but F49 makes nothing from IRON_ORE and XB5C exchanges it: H51, which makes IRON from it, comes
        // first, even with D48 putting the ores no miner works on first.
        var context = new MiningContext(IronMap(), [], 129_357, Now);

        var targets = MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>(), []);

        targets.Select(target => (target.SellWaypointSymbol, target.FeedsProduction)).Should().Equal((H51, true), (XB5C, false), (F49, false));
        targets.Should().OnlyContain(target => target.Ore == "IRON_ORE");
    }

    [Fact]
    public void AMarketThatOnlyPaysForAnOre_IsNoScarceOre_NoOpening_AndComesLastToShare()
    {
        // D91: F49 and XB5C are SCARCE of IRON_ORE but only pay for it. No drone is wanted for them (D48), they are no
        // opening that waits for a ship, and a drone shares H51's pair, however many mine it, before it mines for them.
        var context = new MiningContext(IronMap(), [], 129_357, Now);

        MiningPlanner.ScarceOres(context, Drone()).Select(area => (area.Good, string.Join(',', area.MarketSymbols))).Should().Equal(("IRON_ORE", H51));
        MiningPlanner.LowSupplyOpportunities(IronMap()).Select(opening => opening.SellWaypointSymbol).Should().Equal(H51);
        MiningPlanner.SharedTargets(context, Drone(), new Dictionary<string, int> { [MiningPlanner.OpportunityKey(H51, "IRON_ORE")] = 3 })
            .Select(target => target.SellWaypointSymbol).Should().Equal(H51, XB5C, F49);
    }

    [Fact]
    public void AMarketMakesSomethingFromAGood_ThatItImportsAndExportsAGoodMadeOf_AnyImportWithoutTheChains()
    {
        // D91: H51 makes IRON from IRON_ORE; F49 imports it and makes nothing from it; XB5C exchanges it. Without the
        // production chains, as in the fixture's map, any import counts, as before.
        var map = IronMap();

        (map.MakesSomethingFrom(H51, "IRON_ORE"), map.MakesSomethingFrom(F49, "IRON_ORE"), map.MakesSomethingFrom(XB5C, "IRON_ORE")).Should().Be((true, false, false));
        (map.Exchanges(XB5C, "IRON_ORE"), map.Exchanges(H51, "IRON_ORE"), map.Exchanges(F49, "IRON_ORE")).Should().Be((true, false, false));
        Map().MakesSomethingFrom(F49, "QUARTZ_SAND").Should().BeTrue("the fixture's map names no production chains");
    }

    /// <summary>A miner's trip on an ore for a market, by a ship with an 80-unit tank unless said otherwise.</summary>
    private static CoveringTrip Covering(string ore, string market, int tank = 80) => new(ore, market, tank);

    /// <summary>Drones on silicon and quartz for F49, and on copper and iron for H51: every ore short near the middle.</summary>
    private static CoveringTrip[] MiddleCovered()
        => [Covering("SILICON_CRYSTALS", F49), Covering("QUARTZ_SAND", F49), Covering("COPPER_ORE", H51), Covering("IRON_ORE", H51)];

    /// <summary>The fixture's markets, with H51's aluminum at the supply given: MODERATE in the fixture.</summary>
    private static TradeMarketMap MapWithAluminumAtH51(string supply)
        => Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != H51),
            Market(
                H51,
                Good("COPPER_ORE", "IMPORT", 138, 67, 123, "LIMITED"),
                Good("IRON_ORE", "IMPORT", 118, 58, 100, "LIMITED"),
                Good("ALUMINUM_ORE", "IMPORT", 130, 63, 149, supply),
                Good("FUEL", "EXCHANGE", 95, 80, 180, "MODERATE")),
        ]);

    [Fact]
    public void AFarAsteroidNoDroneMinesOnARoundTripOfItsMarket_IsACollectionPoint()
    {
        // D83, asked on 2026-10-05: "We park a light shuttle ... at the asteroid, and have the drones drop their ore into the
        // light shuttle." B7's iron comes only from B13, 48 away: a drone gets there from B7 on a full tank, but not back
        // (D45), and a shuttle's 300-unit tank flies there and back. B7's gold comes from B14, on a drone's round trip.
        var points = MiningPlanner.CollectionPoints(MapWithIronAtB7(), Drone(), Shuttle());

        var point = points.Should().ContainSingle().Subject;
        (point.AsteroidSymbol, point.SellWaypointSymbol).Should().Be((B13, B7));
        point.Ores.Should().Equal("IRON_ORE");
        point.ScarceOres.Should().Equal("IRON_ORE");
        point.DronesWanted.Should().Be(1);
    }

    [Fact]
    public void AnOreAtCollectionPoint_CountsWhileTheMarketHasItBelowAbundant_ADroneOnlyWhileItIsShort()
    {
        // D77: nobody mines for a market that has an ore ABUNDANT; D48: a drone per SCARCE or LIMITED ore.
        MiningPlanner.CollectionPoints(MapWithIronAtB7(supply: "ABUNDANT"), Drone(), Shuttle()).Should().BeEmpty();

        var point = MiningPlanner.CollectionPoints(MapWithIronAtB7(supply: "MODERATE"), Drone(), Shuttle()).Should().ContainSingle().Subject;
        point.Ores.Should().Equal("IRON_ORE");
        point.ScarceOres.Should().BeEmpty();
        point.DronesWanted.Should().Be(0);
    }

    [Fact]
    public void WhereADroneMinesOnARoundTrip_OrNoShuttleFliesThereAndBack_ThereIsNoCollectionPoint()
    {
        MiningPlanner.CollectionPoints(Map(), Drone(), Shuttle()).Should().BeEmpty("B14 is on a drone's round trip of B7 (D45)");
        MiningPlanner.CollectionPoints(MapWithIronAtB7(), Drone(), Drone(symbol: "SHIP-7")).Should().BeEmpty("an 80-unit tank doesn't fly the 96 to B13 and back");
    }

    /// <summary>A light shuttle: a 40-unit hold and a 300-unit tank, nothing to mine with.</summary>
    private static ShipModel Shuttle()
        => new("SHIP-7", SystemSymbol, H52, "DOCKED", "CRUISE", 300, 300, CargoCapacity: 40, ShipType: "SHIP_LIGHT_SHUTTLE", MountSymbols: [], CargoInventory: []);

    /// <summary>The fixture's markets, with B7 importing iron as well, which only B13, 48 from B7, yields near it.</summary>
    private static TradeMarketMap MapWithIronAtB7(string supply = "LIMITED")
        => Map(
        [
            .. Markets().Where(market => market.WaypointSymbol != B7),
            Market(
                B7,
                Good("GOLD_ORE", "IMPORT", 230, 114, 60, "SCARCE"),
                Good("IRON_ORE", "IMPORT", 118, 61, 60, supply),
                Good("FUEL", "EXCHANGE", 79, 71, 180, "MODERATE")),
        ]);
}

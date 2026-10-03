using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.4: what a surveyor surveys (the contract's ore first, then sellable ores near the market that
/// buys them, each until it has a stock of usable surveys, D27) and what a miner mines (the markets shortest
/// of an ore first, D28; within a supply level, surveyed ores first).
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
        // mostly aluminum doesn't put H51's aluminum (MODERATE) before the scarce and limited markets.
        var context = Context(Survey("S-1", XB5C, "ALUMINUM_ORE", "ALUMINUM_ORE", "ALUMINUM_ORE", "COPPER_ORE"));

        var targets = MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>());

        targets.Select(target => (target.Ore, target.SellWaypointSymbol, target.Supply)).Should().Equal(
            ("SILICON_CRYSTALS", F49, "SCARCE"),
            ("QUARTZ_SAND", F49, "SCARCE"),
            ("COPPER_ORE", H51, "LIMITED"),
            ("IRON_ORE", H51, "LIMITED"),
            ("ALUMINUM_ORE", H51, "MODERATE"));
        targets.Select(target => target.LowSupply).Should().Equal(true, true, true, true, false);
    }

    [Fact]
    public void WithoutSurveys_AMiner_MinesAtTheAsteroidNearestEachMarket_TheLowestSupplyFirst_AndSellsItThere()
    {
        // F49 is SCARCE of silicon and quartz, H51 LIMITED of copper and iron and MODERATE of aluminum (D28).
        // B7's markets are beyond a drone's tank.
        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>());

        targets.Select(target => (target.Ore, target.AsteroidSymbol, target.SellWaypointSymbol)).Should().Equal(
            ("SILICON_CRYSTALS", XB5C, F49),
            ("QUARTZ_SAND", XB5C, F49),
            ("COPPER_ORE", XB5C, H51),
            ("IRON_ORE", XB5C, H51),
            ("ALUMINUM_ORE", XB5C, H51));
        targets[0].Share.Should().BeApproximately(1 / 6.0, 1e-9, "XB5C yields six ores");
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
        var covered = new HashSet<string> { "SILICON_CRYSTALS" };

        var targets = MiningPlanner.MiningTargets(new MiningContext(map, [], 129_357, Now), Drone(), held, covered);

        targets.Select(target => (target.Ore, target.SellWaypointSymbol)).Should().Equal(
            ("QUARTZ_SAND", F49),
            ("COPPER_ORE", H51),
            ("IRON_ORE", H51),
            ("SILICON_CRYSTALS", H52),
            ("ALUMINUM_ORE", H51));
    }

    [Fact]
    public void OfTheScarceOresNoMinerWorksOn_TheNearestAsteroidComesFirst()
    {
        // D48, "near before far": an asteroid with mineral deposits 5 from H51 yields iron, which D28 alone would put after
        // copper, as copper fetches more; copper comes from XB5C, 19 away.
        var nearby = new WaypointCacheModel("X1-DC53-XN1", SystemSymbol, "ASTEROID", -18, 45, false, false, DateTimeOffset.UnixEpoch, TraitsJson: """[{"symbol":"MINERAL_DEPOSITS"}]""");
        var map = new TradeMarketMap([.. Waypoints, nearby], Markets(), new Dictionary<string, IReadOnlyList<string>>());
        var covered = new HashSet<string> { "SILICON_CRYSTALS", "QUARTZ_SAND" };

        var targets = MiningPlanner.MiningTargets(new MiningContext(map, [], 129_357, Now), Drone(), new HashSet<string>(), covered);

        targets.Select(target => (target.Ore, target.AsteroidSymbol)).Take(2).Should().Equal(("IRON_ORE", "X1-DC53-XN1"), ("COPPER_ORE", XB5C));
    }

    [Fact]
    public void TheScarceOres_AreThoseADroneCouldServeAMarketShortOf_WhoeverWorksOnThem()
    {
        // B7's gold and copper are beyond a drone's tank, so they don't count for it; the command ship reaches them.
        MiningPlanner.ScarceOres(Context(), Drone()).Should().BeEquivalentTo(["SILICON_CRYSTALS", "QUARTZ_SAND", "COPPER_ORE", "IRON_ORE"]);
        MiningPlanner.ScarceOres(Context(), CommandShip()).Should().Contain("GOLD_ORE");
    }

    [Fact]
    public void ADrone_CantReachTheFarAsteroids()
    {
        MiningPlanner.CanReach(Map(), Drone(), XB5C).Should().BeTrue();
        MiningPlanner.CanReach(Map(), Drone(), B14).Should().BeFalse();
        MiningPlanner.CanReach(Map(), CommandShip(), B14).Should().BeTrue();
    }
}

using FluentAssertions;
using SpaceTraders.Application.Mining;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.4: what a surveyor surveys (the contract's ore first, then sellable ores near the market that
/// buys them, each until it has a stock of usable surveys, D27) and what a miner mines (surveyed ores
/// first, then ores in low supply, D22).
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
    public void AMiner_MinesASurveyedOreFirst_TheOneASingleExtractionFetchesMostFor()
    {
        // Copper is two of three deposits: 2/3 x 67 beats iron's 1/3 x 58.
        var context = Context(Survey("S-1", XB5C, "COPPER_ORE", "COPPER_ORE", "IRON_ORE"));

        var targets = MiningPlanner.MiningTargets(context, Drone(), new HashSet<string>());

        targets[0].Should().BeEquivalentTo(new MiningTarget("COPPER_ORE", XB5C, H51, 67, 2 / 3.0, Surveyed: true, LowSupply: true));
        targets[1].Ore.Should().Be("IRON_ORE");
        targets[1].Surveyed.Should().BeTrue();
        targets.Skip(2).Should().OnlyContain(target => !target.Surveyed);
    }

    [Fact]
    public void WithoutSurveys_AMiner_MinesAnOreInLowSupply_AtTheAsteroidNearestItsMarket_AndSellsItThere()
    {
        // SCARCE and LIMITED count (D22); ALUMINUM at H51 is MODERATE. B7's openings are beyond a drone's tank.
        var targets = MiningPlanner.MiningTargets(Context(), Drone(), new HashSet<string>());

        targets.Select(target => (target.Ore, target.AsteroidSymbol, target.SellWaypointSymbol)).Should().Equal(
            ("COPPER_ORE", XB5C, H51),
            ("IRON_ORE", XB5C, H51),
            ("SILICON_CRYSTALS", XB5C, F49),
            ("QUARTZ_SAND", XB5C, F49));
        targets[0].Share.Should().BeApproximately(1 / 6.0, 1e-9, "XB5C yields six ores");
    }

    [Fact]
    public void AnOpeningAnotherMinerHolds_IsNotOffered()
    {
        var held = new HashSet<string> { MiningPlanner.OpportunityKey(H51, "COPPER_ORE") };

        var targets = MiningPlanner.MiningTargets(Context(), Drone(), held);

        targets[0].Ore.Should().Be("IRON_ORE");
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
    public void ADrone_CantReachTheFarAsteroids()
    {
        MiningPlanner.CanReach(Map(), Drone(), XB5C).Should().BeTrue();
        MiningPlanner.CanReach(Map(), Drone(), B14).Should().BeFalse();
        MiningPlanner.CanReach(Map(), CommandShip(), B14).Should().BeTrue();
    }
}

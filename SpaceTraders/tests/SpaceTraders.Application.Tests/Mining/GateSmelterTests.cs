using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.25 (D92), asked on 2026-10-05: "as part of the jump gate build phase, extra miners to be bought for the ores that
/// supply the build gate materials once every half hour (and those miners being dedicated to those ores) until each of the
/// smelters have at least HIGH saturation." Asked which ores count: "Smelters only": the markets that smelt an ore into a
/// metal a material the gate still needs is made from, IRON_ORE into IRON for FAB_MATS, COPPER_ORE into COPPER for
/// ADVANCED_CIRCUITRY.
/// </summary>
public sealed class GateSmelterTests
{
    [Fact]
    public void AGood_GoesIntoTheGate_ThroughEveryStepOfTheProductionChains()
    {
        var map = GateFixture.Map(["ADVANCED_CIRCUITRY"]);

        map.GoesIntoConstruction("ADVANCED_CIRCUITRY").Should().BeTrue();
        map.GoesIntoConstruction("ELECTRONICS").Should().BeTrue();
        map.GoesIntoConstruction("COPPER").Should().BeTrue();
        map.GoesIntoConstruction("COPPER_ORE").Should().BeTrue();
        map.GoesIntoConstruction("IRON").Should().BeFalse("FAB_MATS is done");
        GateFixture.Map([]).GoesIntoConstruction("COPPER").Should().BeFalse("the gate needs nothing");
        map.InputsOf("IRON").Should().Equal("IRON_ORE");
        map.InputsOf("IRON_ORE").Should().BeEmpty();
    }

    [Fact]
    public void TheGatesSmelters_AreTheMarketsThatSmeltAnOre_IntoAMetalAMaterialItNeedsIsMadeFrom()
    {
        // H52 makes FAB_MATS from QUARTZ_SAND and B7 ELECTRONICS from SILICON_CRYSTALS: factories, not smelters. F49's
        // ALUMINUM goes into nothing the gate needs.
        MiningPlanner.GateSmelters(GateFixture.Map())
            .Select(smelter => (smelter.MarketSymbol, smelter.Ore, smelter.Metal, smelter.Supply))
            .Should().Equal((F49, "COPPER_ORE", "COPPER", "MODERATE"), (H51, "IRON_ORE", "IRON", "MODERATE"));
    }

    [Fact]
    public void OnlyTheSmeltersOfWhatTheGateStillNeeds_Count_AndNoneOnceItNeedsNothing()
    {
        MiningPlanner.GateSmelters(GateFixture.Map(["FAB_MATS"])).Select(smelter => smelter.MarketSymbol).Should().Equal(H51);
        MiningPlanner.GateSmelters(GateFixture.Map(["ADVANCED_CIRCUITRY"])).Select(smelter => smelter.MarketSymbol).Should().Equal(F49);
        MiningPlanner.GateSmelters(GateFixture.Map([])).Should().BeEmpty();
    }

    [Fact]
    public void WithoutTheProductionChains_NoMarketIsASmelter()
    {
        var map = new TradeMarketMap(Waypoints, GateFixture.Markets(), new Dictionary<string, IReadOnlyList<string>>())
        {
            ConstructionMaterials = new HashSet<string>(GateFixture.GateMaterials, StringComparer.OrdinalIgnoreCase),
        };

        MiningPlanner.GateSmelters(map).Should().BeEmpty();
    }

    [Fact]
    public void TheOresShortAtTheGatesSmelters_TheLowestSupplyFirst_UntilEachHasItHigh()
    {
        // "until each of the smelters have at least HIGH saturation".
        Short(GateFixture.Markets(iron: "MODERATE", copper: "LIMITED")).Should().Equal("COPPER_ORE", "IRON_ORE");
        Short(GateFixture.Markets(iron: "SCARCE", copper: "LIMITED")).Should().Equal("IRON_ORE", "COPPER_ORE");
        Short(GateFixture.Markets(iron: "HIGH", copper: "LIMITED")).Should().Equal("COPPER_ORE");
        Short(GateFixture.Markets(iron: "HIGH", copper: "ABUNDANT")).Should().BeEmpty();
    }

    [Fact]
    public void ASmelterNoDroneCanServe_WantsNoDrone()
    {
        // B7 smelts COPPER here, out of a drone's CRUISE reach, and sells no fuel to drift to (D45). F49 smelts none.
        var markets = GateFixture.Markets(copper: "HIGH")
            .Select(market => market.WaypointSymbol == B7
                ? Market(B7, Good("COPPER_ORE", "IMPORT", 138, 67, 60, "SCARCE"), Good("COPPER", "EXPORT", 400, 200, 60, "MODERATE"))
                : market)
            .ToArray();

        Short(markets, ["ADVANCED_CIRCUITRY"]).Should().BeEmpty();
        MiningPlanner.GateSmelters(GateFixture.Map(["ADVANCED_CIRCUITRY"], markets)).Select(smelter => smelter.MarketSymbol).Should().Equal(B7, F49);
    }

    [Fact]
    public void AGateMiner_MinesItsOre_ForItsSmelterShortestOfIt_SharingWithOtherMiners()
    {
        // F49 smelts IRON too here, and has its IRON_ORE LIMITED; H51 has a miner already, which doesn't keep a gate miner out.
        var markets = GateFixture.Markets()
            .Select(market => market.WaypointSymbol == F49
                ? market with { TradeGoods = [.. market.TradeGoods, Good("IRON_ORE", "IMPORT", 124, 62, 60, "LIMITED"), Good("IRON", "EXPORT", 300, 145, 60, "MODERATE")] }
                : market)
            .ToArray();
        var context = new MiningContext(GateFixture.Map(markets: markets), [], 1_000_000, Now);
        var minersOn = new Dictionary<string, int> { [MiningPlanner.OpportunityKey(H51, "IRON_ORE")] = 1 };

        MiningPlanner.GateTargets(context, Drone(), "IRON_ORE", minersOn)
            .Select(target => (target.Ore, target.SellWaypointSymbol))
            .Should().Equal(("IRON_ORE", F49), ("IRON_ORE", H51));
    }

    [Fact]
    public void AGateMiner_HasNoTarget_AtASmelterThatHasItsOreAbundant_NorOnceTheGateNeedsNothingMadeFromIt()
    {
        // D77: an ABUNDANT market is mined for by nobody.
        var abundant = new MiningContext(GateFixture.Map(markets: GateFixture.Markets(iron: "ABUNDANT")), [], 1_000_000, Now);
        var done = new MiningContext(GateFixture.Map(["ADVANCED_CIRCUITRY"]), [], 1_000_000, Now);
        var none = new Dictionary<string, int>();

        MiningPlanner.GateTargets(abundant, Drone(), "IRON_ORE", none).Should().BeEmpty();
        MiningPlanner.GateTargets(done, Drone(), "IRON_ORE", none).Should().BeEmpty();
        MiningPlanner.GateTargets(done, Drone(), "COPPER_ORE", none).Select(target => target.SellWaypointSymbol).Should().Equal(F49);
    }

    [Theory]
    [InlineData("SCARCE", false)]
    [InlineData("LIMITED", false)]
    [InlineData("MODERATE", false)]
    [InlineData("HIGH", true)]
    [InlineData("ABUNDANT", true)]
    [InlineData("", true)]
    public void ASmelterHasEnough_FromHigh(string supply, bool enough)
        => MiningPlanner.HasEnough(supply).Should().Be(enough);

    /// <summary>The ores short at the gate's smelters for a drone bought at H52.</summary>
    private static IReadOnlyList<string> Short(MarketSnapshot[] markets, IEnumerable<string>? materials = null)
        => MiningPlanner.GateOresShort(new MiningContext(GateFixture.Map(materials, markets), [], 1_000_000, Now), Drone("NEW-DRONE", H52));
}

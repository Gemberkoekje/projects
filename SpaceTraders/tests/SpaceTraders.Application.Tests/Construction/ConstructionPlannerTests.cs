using FluentAssertions;
using SpaceTraders.Application.Construction;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Construction.ConstructionFixture;

namespace SpaceTraders.Application.Tests.Construction;

/// <summary>
/// Slice 6.6, asked on 2026-10-04: "Finishing this jump node should be top priority, as it opens up the rest of the game."
/// What the jump gate still needs, and which load a builder takes: a full hold, or what the gate still needs, in one
/// purchase (D62), where the supply isn't SCARCE or LIMITED (D61), at the market where it costs least with its fuel.
/// </summary>
public sealed class ConstructionPlannerTests
{
    [Fact]
    public void WhatTheGateNeeds_LeavesOutWhatIsSupplied_AndWhatOurTripsCarryOrGoToBuy()
    {
        // QUANTUM_STABILIZERS come supplied. SHIP-6 carries 80 FAB_MATS there and SHIP-7 goes to buy 40; a blocked trip, and
        // a trip to another site, carry nothing for this one.
        var needs = ConstructionPlanner.Needs(
            Site(fabMats: 200),
            [
                Trip("FAB_MATS", 80, bought: true),
                Trip("FAB_MATS", 40),
                Trip("FAB_MATS", 40) with { Status = GoalStatus.Blocked },
                Trip("ADVANCED_CIRCUITRY", 40) with { ConstructionSiteWaypointSymbol = "X1-HZ59-I59" },
            ]);

        needs.Select(need => (need.TradeSymbol, need.Remaining)).Should().Equal(("FAB_MATS", 1_600 - 200 - 120), ("ADVANCED_CIRCUITRY", 400));
    }

    [Fact]
    public void AHaulerWithAnEmptyHold_BuysAFullHoldOfFabMats_WhereItCostsLeast()
    {
        // ADVANCED_CIRCUITRY comes first by name at an equal share, but D42 trades 40 at a time, under the hauler's 80 (D62);
        // A1's FAB_MATS are LIMITED (D61), and dearer anyway.
        var loads = ConstructionPlanner.Loads(Map(), Hauler(), Gate, ConstructionPlanner.Needs(Site(), []));

        var load = loads.Should().ContainSingle().Subject;
        (load.TradeSymbol, load.BuyWaypointSymbol, load.Units, load.UnitPrice).Should().Be(("FAB_MATS", F49, 80, 2_100));
        load.CargoCost.Should().Be(168_000);

        // H51 to F49 is 52 fuel, bought at F49 (82 a unit of 100); F49 to the gate 495, bought at the gate (5 units at 90).
        load.FuelCost.Should().Be(82 + (5 * 90));
    }

    [Fact]
    public void WhenTheGateNeedsLessThanAHold_TheLoadIsWhatItNeeds_AndASmallerTradeVolumeTakesIt()
    {
        // 30 ADVANCED_CIRCUITRY left: D42's 40 at a time take them in one purchase.
        var loads = ConstructionPlanner.Loads(Map(), Hauler(), Gate, ConstructionPlanner.Needs(Site(fabMats: 1_600, circuitry: 370), []));

        var load = loads.Should().ContainSingle().Subject;
        (load.TradeSymbol, load.BuyWaypointSymbol, load.Units).Should().Be(("ADVANCED_CIRCUITRY", D42, 30));
    }

    [Fact]
    public void TheMaterialWithTheSmallestShare_ComesFirst()
    {
        // A shuttle's 40 fit both markets' trade volumes. FAB_MATS are at 50%, ADVANCED_CIRCUITRY at 25%.
        var loads = ConstructionPlanner.Loads(Map(), Shuttle(), Gate, ConstructionPlanner.Needs(Site(fabMats: 800, circuitry: 100), []));

        loads.Select(load => load.TradeSymbol).Should().Equal("ADVANCED_CIRCUITRY", "FAB_MATS");
    }

    [Theory]
    [InlineData("SCARCE", false)]
    [InlineData("LIMITED", false)]
    [InlineData("MODERATE", true)]
    [InlineData("HIGH", true)]
    public void AMarketShortOfTheMaterial_SellsNoLoad(string supply, bool bought)
    {
        // D61: "Don't buy where the material is SCARCE or LIMITED; wait until the market recovers to MODERATE."
        var map = Map(GateMarket(), F49Market(supply: supply), D42Market(), H51Market(), I56Market());

        var loads = ConstructionPlanner.Loads(map, Hauler(), Gate, ConstructionPlanner.Needs(Site(), []));

        loads.Any(load => load.TradeSymbol == "FAB_MATS").Should().Be(bought);
    }

    [Fact]
    public void AMarketWhoseTradeVolumeIsUnderAHold_SellsNoLoad_ButTheCreditsAreSavedUpForIt()
    {
        // D62, asked on 2026-10-04: "If the markets trade volume is smaller than a haulers hold, it should wait until the trade
        // volume is a haulers hold." Not strict, it is what the plan waits for.
        var map = Map(GateMarket(), F49Market(tradeVolume: 60), D42Market(), H51Market(), I56Market());
        var needs = ConstructionPlanner.Needs(Site(), []);

        ConstructionPlanner.Loads(map, Hauler(), Gate, needs).Should().BeEmpty();
        ConstructionPlanner.Loads(map, Hauler(), Gate, needs, strict: false).Select(load => (load.TradeSymbol, load.Units)).Should().Equal(("ADVANCED_CIRCUITRY", 80), ("FAB_MATS", 80));
        ConstructionPlanner.WhyNoLoad(map, Hauler(), Gate, needs).Should().Be(ConstructionPlanner.TradeVolume);
    }

    [Fact]
    public void WithEveryMarketShort_TheBuilderWaitsForTheSupply()
    {
        var map = Map(GateMarket(), F49Market(supply: "LIMITED"), D42Market(supply: "SCARCE"), A1Market(), H51Market(), I56Market());

        ConstructionPlanner.WhyNoLoad(map, Hauler(), Gate, ConstructionPlanner.Needs(Site(), [])).Should().Be(ConstructionPlanner.LowSupply);
    }

    [Fact]
    public void WithoutAMarketThatSellsWhatTheGateNeeds_ThereIsNoLoad()
    {
        var map = Map(GateMarket(), H51Market(), I56Market());

        ConstructionPlanner.Loads(map, Hauler(), Gate, ConstructionPlanner.Needs(Site(), []), strict: false).Should().BeEmpty();
        ConstructionPlanner.WhyNoLoad(map, Hauler(), Gate, ConstructionPlanner.Needs(Site(), [])).Should().Be(ConstructionPlanner.NoMarket);
    }

    [Fact]
    public void OfTwoMarkets_TheLoadGoesWhereItCostsLeastWithItsFuel()
    {
        // A1 recovered to MODERATE and asks 2,000, under F49's 2,100: 80 units save 8,000, more than the fuel differs.
        var map = Map(GateMarket(), F49Market(), A1Market(supply: "MODERATE", price: 2_000), D42Market(), H51Market(), I56Market());

        var load = ConstructionPlanner.Loads(map, Hauler(), Gate, ConstructionPlanner.Needs(Site(), [])).Single(candidate => candidate.TradeSymbol == "FAB_MATS");

        load.BuyWaypointSymbol.Should().Be(A1);
    }

    [Fact]
    public void AShortTank_FliesToTheGateThroughARefuellingStop()
    {
        // The shuttle's 300 don't take it from F49 to the gate (495); by way of I56 (278, then 219) they do.
        var load = ConstructionPlanner.Loads(Map(), Shuttle(), Gate, ConstructionPlanner.Needs(Site(), [])).Single(candidate => candidate.TradeSymbol == "FAB_MATS");

        load.FuelCost.Should().Be(82 + (3 * 80) + (3 * 90));
    }

    [Fact]
    public void AShipWithoutRoom_TakesNoLoad()
    {
        var full = Hauler(cargo: [new CargoItemModel("IRON", 80)]);

        ConstructionPlanner.Loads(Map(), full, Gate, ConstructionPlanner.Needs(Site(), [])).Should().BeEmpty();
    }

    [Fact]
    public void AShipThatHoldsWhatTheGateNeeds_SuppliesAsMuchOfItAsTheGateStillNeeds()
    {
        var ship = Hauler(cargo: [new CargoItemModel("IRON", 10), new CargoItemModel("FAB_MATS", 50), new CargoItemModel("ADVANCED_CIRCUITRY", 20)]);

        ConstructionPlanner.TryFindDelivery(ship, ConstructionPlanner.Needs(Site(fabMats: 1_570), []), out var good, out var units).Should().BeTrue();

        (good, units).Should().Be(("FAB_MATS", 30));
        ConstructionPlanner.TryFindDelivery(Hauler(cargo: [new CargoItemModel("IRON", 10)]), ConstructionPlanner.Needs(Site(), []), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void WithTheRoleBoardOff_TheLargestHoldsBuild_NeverADroneOrAProbe()
    {
        var probe = new ShipModel("SHIP-2", SystemSymbol, H51, "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE");

        ConstructionPlanner.PickBuilders([CommandShip(), probe, Drone(), Shuttle(), Hauler()], 1).Select(ship => ship.Symbol).Should().Equal("SHIP-6");

        // Of two 40-unit holds, the shuttle, which can do least else, before the command ship.
        ConstructionPlanner.PickBuilders([CommandShip(), Drone(), Shuttle()], 1).Select(ship => ship.Symbol).Should().Equal("SHIP-7");
        ConstructionPlanner.PickBuilders([Drone(), probe], 1).Should().BeEmpty();
    }

    [Fact]
    public void TheSystemOfAWaypoint_IsItsSymbolUpToTheLastDash()
    {
        ConstructionPlanner.SystemOf(Gate).Should().Be(SystemSymbol);
        ConstructionPlanner.NeedsMaterials(Site()).Should().BeTrue();
        ConstructionPlanner.NeedsMaterials(Site(fabMats: 1_600, circuitry: 400)).Should().BeFalse();
        ConstructionPlanner.NeedsMaterials(Site(complete: true)).Should().BeFalse();
    }

    private static SupplyConstructionGoal Trip(string good, int units, bool bought = false)
        => new() { TradeSymbol = good, ConstructionSiteWaypointSymbol = Gate, BuyWaypointSymbol = F49, Units = units, CargoBought = bought };
}

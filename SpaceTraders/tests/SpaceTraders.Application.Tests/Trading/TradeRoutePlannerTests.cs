using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.5: a trip's profit is what the sell market pays minus what the buy market charges, times
/// the units, minus the fuel; it is lucrative from <c>Trade.MinProfitPerUnit</c> per unit (D14); routes
/// that feed a pricier good's production come first (D15); two traders never share a route. D79: a trip carries as many
/// units as each earn the minimum, in batches of each market's trade volume, each batch bought a step dearer and each sold a
/// step cheaper; D80: one buyer of a good at a market at a time.
/// </summary>
public sealed class TradeRoutePlannerTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void Profit_IsTheMarginTimesTheUnits_MinusTheFuelForBothLegs()
    {
        // From K85: 185 to D41 (2 FUEL at D41, 76 each), then 95 to A1 (1 FUEL at A1, 90).
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "MEDICINE", D41, A1, 250_000, 200, out var route).Should().BeTrue();

        route.Units.Should().Be(40, "every unit earns 386, and both markets trade MEDICINE 40 at a time: one batch each (D79)");
        route.BuyPrice.Should().Be(4_867);
        route.SellPrice.Should().Be(5_253);
        route.FuelCost.Should().Be((2 * 76) + 90);
        route.Profit.Should().Be((386 * 40) - 242);
        route.FeedsTradeSymbol.Should().BeEmpty("A1 makes nothing from MEDICINE");
    }

    [Fact]
    public void Profit_CountsTheFuelToGetToTheBuyMarketToBeginWith()
    {
        // Your note of 2026-10-02. P and Q sell the same good at the same price, each 100 from R,
        // which buys it; the ship is at P, 200 from Q. Getting to Q costs 2 FUEL more (100 each).
        var map = new TradeMarketMap(
            [Place("X1-AB-P", 0), Place("X1-AB-Q", 200), Place("X1-AB-R", 100)],
            [
                Market("X1-AB-P", Good("GOOD", "EXPORT", 100, 50, 40), Good("FUEL", "EXCHANGE", 100, 90, 180)),
                Market("X1-AB-Q", Good("GOOD", "EXPORT", 100, 50, 40), Good("FUEL", "EXCHANGE", 100, 90, 180)),
                Market("X1-AB-R", Good("GOOD", "IMPORT", 800, 400, 40), Good("FUEL", "EXCHANGE", 100, 90, 180)),
            ],
            MadeFrom);

        var routes = TradeRoutePlanner.Rank(map, CommandShip("X1-AB-P"), 250_000, 0, NoneHeld);

        routes.Select(route => route.BuyWaypointSymbol).Should().Equal("X1-AB-P", "X1-AB-Q");
        routes[0].Profit.Should().Be((300 * 40) - 100);
        routes[1].Profit.Should().Be((300 * 40) - 100 - 200, "the flight to Q is part of the trip");

        static WaypointCacheModel Place(string symbol, int x) => new(symbol, SystemSymbol, "PLANET", x, 0, true, false, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Rank_PutsARouteThatFeedsAPricierGoodFirst_ThoughAnotherEarnsMore()
    {
        // D15: D41 makes SHIP_PARTS (7,721) from EQUIPMENT, so delivering it there grows that production.
        var routes = TradeRoutePlanner.Rank(Map(), CommandShip(), 250_000, 200, NoneHeld);

        routes.Select(route => (route.TradeSymbol, route.BuyWaypointSymbol, route.SellWaypointSymbol)).Should().Equal(
            ("EQUIPMENT", K85, D41),
            ("MEDICINE", D41, A1),
            ("EQUIPMENT", K85, A1));
        routes[0].FeedsTradeSymbol.Should().Be("SHIP_PARTS");
        routes[0].Profit.Should().Be((233 * 40) - (2 * 76));
        routes[1].Profit.Should().BeGreaterThan(routes[0].Profit);
    }

    [Fact]
    public void Rank_WithoutTheProductionChains_GoesByProfitAlone()
    {
        var map = new TradeMarketMap(Waypoints, [K85Market(), D41Market(), A1Market()], new Dictionary<string, IReadOnlyList<string>>());

        var routes = TradeRoutePlanner.Rank(map, CommandShip(), 250_000, 200, NoneHeld);

        routes.Select(route => route.TradeSymbol + " " + route.SellWaypointSymbol).Should().Equal(
            "MEDICINE " + A1,
            "EQUIPMENT " + A1,
            "EQUIPMENT " + D41);
    }

    [Fact]
    public void Rank_LeavesOutRoutesBelowTheMinimumProfitPerUnit()
    {
        // D14: FOOD K85 to A1 earns 5,100 on 40 units, 127 a unit.
        TradeRoutePlanner.Rank(Map(), CommandShip(), 250_000, 200, NoneHeld)
            .Should().NotContain(route => route.TradeSymbol == "FOOD");

        TradeRoutePlanner.Rank(Map(), CommandShip(), 250_000, 0, NoneHeld)
            .Should().ContainSingle(route => route.TradeSymbol == "FOOD")
            .Which.Profit.Should().Be((132 * 40) - (2 * 90));
    }

    [Fact]
    public void Rank_LeavesOutTheRoutesOtherTradersHold()
    {
        var held = new HashSet<string> { TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41) };

        TradeRoutePlanner.Rank(Map(), CommandShip(), 250_000, 200, held)
            .Should().NotContain(route => route.TradeSymbol == "EQUIPMENT" && route.SellWaypointSymbol == D41)
            .And.HaveCount(2);
    }

    [Fact]
    public void Rank_LeavesOutALegLongerThanTheTankHolds()
    {
        // The drone's tank holds 80: every market is further than that from K85.
        TradeRoutePlanner.Rank(Map(), Drone(), 250_000, 0, NoneHeld).Should().BeEmpty();
    }

    [Fact]
    public void AShipNotDockedWhereFuelIsSold_FliesItsFirstLegOnTheFuelAboard()
    {
        // In orbit at K85 with 100 aboard: D41 is 185 away. Buying at K85 is fine: it docks there to
        // buy, and fills its tank before it leaves.
        var ship = CommandShip(status: "IN_ORBIT", fuel: 100);

        TradeRoutePlanner.TryEvaluate(Map(), ship, "MEDICINE", D41, A1, 250_000, 200, out _).Should().BeFalse();
        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 250_000, 200, out _).Should().BeTrue();
    }

    [Fact]
    public void FromBeyondOneTank_TheTripRefuelsOnTheWay()
    {
        // The scout plan ended at J57: from there one tank reaches only I56. Without refuelling stops
        // the command ship found no route at all on 2026-10-02.
        var map = Map([K85Market(), D41Market(), A1Market(), .. FarMarkets()]);
        var ship = CommandShip(J57, fuel: 252);

        var routes = TradeRoutePlanner.Rank(map, ship, 250_000, 200, NoneHeld);

        // J57 to I56 (368: 4 FUEL at 86), I56 to D41 (312: 4 at 76), D41 to A1 (95: 1 at 90).
        var medicine = routes.Should().ContainSingle(route => route.TradeSymbol == "MEDICINE").Subject;
        medicine.FuelCost.Should().Be((4 * 86) + (4 * 76) + 90);
        medicine.Profit.Should().Be((386 * 40) - 738);
        TradeRoutePlanner.NextStop(map, ship, D41).Should().Be(I56);
    }

    [Fact]
    public void AFlight_TakesTheFewestStops_ThoughAnotherWaySavesAFewCredits()
    {
        // Through A4 (fuel at 75) the flight from J57 to D41 costs 645, through I56 alone 648. Every
        // stop costs a dock, a refuel, an orbit and a market refresh: the live map, 2026-10-02.
        const string a4 = "X1-AB-A4";
        var map = new TradeMarketMap(
            [.. Waypoints, new WaypointCacheModel(a4, SystemSymbol, "ORBITAL_STATION", 21, 16, true, false, DateTimeOffset.UnixEpoch)],
            [K85Market(), D41Market(), A1Market(), .. FarMarkets(), Market(a4, Good("FUEL", "EXCHANGE", 75, 69, 180))],
            MadeFrom);

        TradeRoutePlanner.TryPlanFlight(map, J57, D41, 400, 400, out var flight).Should().BeTrue();

        flight.Stops.Should().Equal(I56, D41);
        flight.FuelCost.Should().Be((4 * 86) + (4 * 76));
        flight.FuelLeft.Should().Be(400 - 312);
    }

    [Fact]
    public void NextStop_IsTheDestinationItself_WhenOneTankWillDo()
        => TradeRoutePlanner.NextStop(Map(), CommandShip(), D41).Should().Be(D41);

    [Fact]
    public void TryPlanFlight_FindsNothing_WhenNoChainOfFuelMarketsReachesTheDestination()
    {
        // The drone's 80-unit tank reaches no market from K85.
        TradeRoutePlanner.TryPlanFlight(Map([K85Market(), D41Market(), A1Market(), .. FarMarkets()]), K85, D41, 80, 80, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ARouteCarries_AsManyUnitsAsEachEarnTheMinimum_EachBatchBoughtAStepDearer()
    {
        // D79, asked on 2026-10-05: "A ship should buy as much as is profitable per trip". D41 sells SHIP_PARTS 15 at a time at
        // 7,721; each further batch is estimated 4% dearer: 8,029.84, then 8,351.03. Where A1 pays 8,000, only the first batch
        // earns anything; where it pays 9,000, all three earn the 200 a unit, and the hold's 40 go.
        TradeRoutePlanner.TryEvaluate(ShipPartsMap(), CommandShip(D41), "SHIP_PARTS", D41, A1, 1_000_000, 200, out var fifteen).Should().BeTrue();
        TradeRoutePlanner.TryEvaluate(ShipPartsSoldFor(9_000), CommandShip(D41), "SHIP_PARTS", D41, A1, 1_000_000, 200, out var forty).Should().BeTrue();

        (fifteen.Units, fifteen.CargoCost, fifteen.Profit).Should().Be((15, 15 * 7_721, ((8_000 - 7_721) * 15) - 90));
        forty.Units.Should().Be(40);
        forty.CargoCost.Should().Be(PriceSteps.CostInBatches(7_721, 40, 15));
        forty.Profit.Should().Be((40 * 9_000) - PriceSteps.CostInBatches(7_721, 40, 15) - 90);
    }

    [Fact]
    public void EachBatchSold_IsEstimatedAStepCheaper()
    {
        // D79: A1 takes EQUIPMENT 20 at a time here. The first 20 sell at 3,499, 245 over K85's 3,254; the next 20 are
        // estimated 2% cheaper, at 3,429.02, 175 over it: under the 200 a unit, so the trip carries 20. Any profit takes 40.
        var map = Map(K85Market(), D41Market(), Market(A1, Good("EQUIPMENT", "IMPORT", 7_052, 3_499, 20), Good("FUEL", "EXCHANGE", 90, 76, 180)));

        TradeRoutePlanner.TryEvaluate(map, CommandShip(), "EQUIPMENT", K85, A1, 1_000_000, 200, out var twenty).Should().BeTrue();
        TradeRoutePlanner.TryEvaluate(map, CommandShip(), "EQUIPMENT", K85, A1, 1_000_000, 0, out var forty).Should().BeTrue();

        twenty.Units.Should().Be(20);
        forty.Units.Should().Be(40);
        forty.Profit.Should().Be((20 * 3_499) + (long)Math.Floor(20 * 3_499 * 0.98) - (40 * 3_254) - 180);
    }

    [Fact]
    public void TheSellersSupply_DoesNotSizeATrip()
    {
        // D79 replaced D74: at any supply a trip carries what earns the minimum, batch by batch.
        TradeRoutePlanner.TryEvaluate(ShipPartsMap(supplyAtD41: "MODERATE"), CommandShip(D41), "SHIP_PARTS", D41, A1, 1_000_000, 200, out var route)
            .Should().BeTrue();

        route.Units.Should().Be(15);
    }

    [Fact]
    public void WithoutTheCreditsForAllTheUnits_TheTripCarriesFewer_TheFuelKeptBack()
    {
        // D79, and D17: cargo may use the credit reserve, but not the trip's fuel. 40 EQUIPMENT at 3,254 and 152 for fuel
        // come to 130,312; one credit less buys 39. A unit and the fuel come to 3,406.
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 130_312, 200, out var forty).Should().BeTrue();
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 130_311, 200, out var fewer).Should().BeTrue();
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 3_405, 200, out var none, out var check).Should().BeFalse();

        (forty.Units, fewer.Units).Should().Be((40, 39));
        check.Should().Be(TradeRouteCheck.TooFewCredits);
        (none.Units, none.BuyPrice, none.FuelCost).Should().Be((0, 3_254L, 152L));
    }

    [Fact]
    public void ARouteWhoseFirstUnitEarnsTooLittle_IsNoRoute_AndSaysSo()
    {
        // D14 and D79: FOOD from K85 to A1 earns 132 a unit, under the 200.
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "FOOD", K85, A1, 250_000, 200, out var route, out var check).Should().BeFalse();

        check.Should().Be(TradeRouteCheck.NotLucrative);
        (route.Units, route.BuyPrice, route.SellPrice).Should().Be((0, 2_360L, 2_492L));
    }

    [Fact]
    public void TheTripsUnits_StillEarnTheMinimumAUnitAfterFuel()
    {
        // D14 on the whole trip: each of the 15 SHIP_PARTS earns 279, and with the 90 for fuel the trip 4,095, 273 a unit.
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 273, NoneHeld)
            .Should().ContainSingle(route => route.TradeSymbol == "SHIP_PARTS" && route.Units == 15);
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 274, NoneHeld).Should().BeEmpty();
    }

    [Theory]
    [InlineData(3_254, 0, 40, 20)]
    [InlineData(3_254, 20, 20, 0)]
    [InlineData(3_200, 0, 40, 40)]
    [InlineData(3_200, 0, 25, 25)]
    public void UnitsWorthBuying_StopAtTheFirstWhoseSaleWouldEarnTooLittle(long quote, int bought, int most, int units)
    {
        // D79 at the buy market: A1 pays 3,499 for the first 20 and an estimated 3,429.02 for the next 20, 20 at a time.
        var atA1 = Good("EQUIPMENT", "IMPORT", 7_052, 3_499, 20);

        TradeRoutePlanner.UnitsWorthBuying(quote, atA1, bought, most, 200).Should().Be(units);
    }

    [Fact]
    public void Rank_LeavesOutEveryRouteOfAGood_AtAMarketWhereAnotherTripIsOnItsWayToBuyIt()
    {
        // D80, "One buyer at a time": another ship flies to D41 for SHIP_PARTS to sell at K85. Its purchase would raise the
        // price this route was worked out with, so no trip of SHIP_PARTS from D41 is offered, to A1 either.
        var held = HeldBuys.Of([ShipPartsTripFromD41()], []);

        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, held).Should().BeEmpty();
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, HeldBuys.None).Should().ContainSingle();
    }

    [Fact]
    public void AConstructionTripOnItsWayToBuy_HoldsItsMaterial_AtItsBuyMarket()
    {
        // D80 for the jump gate's loads too (slice 6.6).
        var held = HeldBuys.Of([], [new SupplyConstructionGoal { TradeSymbol = "SHIP_PARTS", ConstructionSiteWaypointSymbol = "X1-AB-I55", BuyWaypointSymbol = D41, Units = 15 }]);

        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, held).Should().BeEmpty();
    }

    [Fact]
    public void ATripThatHasBought_OrIsBlocked_HoldsNothing()
    {
        // D80: as with the credits it holds back (D57), a trip holds its good until its cargo is aboard, and a blocked trip
        // will buy nothing.
        var bought = HeldBuys.Of([ShipPartsTripFromD41() with { CargoBought = true }], []);
        var blocked = HeldBuys.Of([ShipPartsTripFromD41() with { Status = GoalStatus.Blocked }], []);

        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, bought).Should().ContainSingle();
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, blocked).Should().ContainSingle();
    }

    [Fact]
    public void Judge_FindsRanksRoutesLucrative_AndForEveryOtherRouteWithAPriceGap_TheCheckItFails()
    {
        // Slice 2.18 (D76), asked on 2026-10-05: "Can the new list also add why the other goods are not considered for
        // trading?" From K85, Rank's routes are EQUIPMENT for D41 and A1, and MEDICINE. FOOD earns 127 a unit (D14); D41 sells
        // FUEL at 76 and K85 buys it at 79, which doesn't pay for the fuel. SHIP_PARTS, which no market here buys, and EQUIPMENT
        // from D41, which no market buys for more, have no price gap.
        var judged = TradeRoutePlanner.Judge(Map(), CommandShip(), 250_000, 200, NoneHeld);

        judged.Select(judgement => (judgement.Route.TradeSymbol, judgement.Route.BuyWaypointSymbol, judgement.Route.SellWaypointSymbol, judgement.Check))
            .Should().BeEquivalentTo(
            [
                ("EQUIPMENT", K85, D41, TradeRouteCheck.Lucrative),
                ("EQUIPMENT", K85, A1, TradeRouteCheck.Lucrative),
                ("MEDICINE", D41, A1, TradeRouteCheck.Lucrative),
                ("FOOD", K85, A1, TradeRouteCheck.NotLucrative),
                ("FUEL", D41, K85, TradeRouteCheck.NotLucrative),
            ]);
        judged.Where(judgement => judgement.Check == TradeRouteCheck.Lucrative).Select(judgement => judgement.Route)
            .Should().BeEquivalentTo(TradeRoutePlanner.Rank(Map(), CommandShip(), 250_000, 200, NoneHeld));
        judged.Single(judgement => judgement.Route.TradeSymbol == "FOOD").Route.Units.Should().Be(0, "its first unit earns 132, under the 200 (D79)");
    }

    [Fact]
    public void Judge_TellsWhichMarketIsOutOfReach()
    {
        // The drone's 80-unit tank reaches no market from K85: it can buy where it is, but not fly on to D41 or A1.
        var judged = TradeRoutePlanner.Judge(Map(), Drone(), 250_000, 0, NoneHeld);

        judged.Select(judgement => (judgement.Route.TradeSymbol, judgement.Route.BuyWaypointSymbol, judgement.Route.SellWaypointSymbol, judgement.Check))
            .Should().BeEquivalentTo(
            [
                ("EQUIPMENT", K85, D41, TradeRouteCheck.SellMarketOutOfReach),
                ("EQUIPMENT", K85, A1, TradeRouteCheck.SellMarketOutOfReach),
                ("FOOD", K85, A1, TradeRouteCheck.SellMarketOutOfReach),
                ("MEDICINE", D41, A1, TradeRouteCheck.BuyMarketOutOfReach),
                ("FUEL", D41, K85, TradeRouteCheck.BuyMarketOutOfReach),
            ]);
    }

    [Fact]
    public void Judge_TellsARouteTheCreditsDontPayAUnitOf_WithThePriceAndTheFuel()
    {
        // A unit of EQUIPMENT at 3,254 and 152 for fuel come to 3,406; FOOD, which the credits pay a unit of, earns too little.
        var judged = TradeRoutePlanner.Judge(Map(), CommandShip(), 3_405, 200, NoneHeld);

        var equipment = judged.Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41));
        equipment.Check.Should().Be(TradeRouteCheck.TooFewCredits);
        (equipment.Route.BuyPrice, equipment.Route.FuelCost).Should().Be((3_254L, 152L));
        equipment.Credits.Should().Be(3_405);
        judged.Single(judgement => judgement.Route.TradeSymbol == "FOOD").Check.Should().Be(TradeRouteCheck.NotLucrative);
    }

    [Fact]
    public void Judge_LeavesOutTheRoutesOfAGoodAnotherTripIsOnItsWayToBuyThere()
    {
        // D80, as a held route is: the good is bought by the trip that holds it.
        TradeRoutePlanner.Judge(ShipPartsMap(), CommandShip(D41), 1_000_000, 200, NoneHeld, HeldBuys.Of([ShipPartsTripFromD41()], []))
            .Should().NotContain(judgement => judgement.Route.TradeSymbol == "SHIP_PARTS" && judgement.Route.BuyWaypointSymbol == D41);
    }

    [Fact]
    public void Judge_LeavesOutTheRoutesOtherTradersHold()
    {
        var held = new HashSet<string> { TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41) };

        TradeRoutePlanner.Judge(Map(), CommandShip(), 250_000, 200, held)
            .Should().NotContain(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41))
            .And.HaveCount(4);
    }

    [Fact]
    public void Units_AreTheFreeHold()
    {
        var ship = CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 35)]);

        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 250_000, 200, out var route).Should().BeTrue();

        route.Units.Should().Be(5);
    }

    [Fact]
    public void TryFindBestSale_PicksTheMarketThatPaysMostAfterFuel()
    {
        var ship = CommandShip(cargo: [new CargoItemModel("EQUIPMENT", 10)]);

        TradeRoutePlanner.TryFindBestSale(Map(), ship, "EQUIPMENT", 10, out var sale).Should().BeTrue();

        // K85 itself pays 1,456; D41 3,487 for 152 of fuel; A1 3,499 for 180.
        sale.WaypointSymbol.Should().Be(A1);
        sale.NetRevenue.Should().Be((3_499 * 10) - 180);
    }

    [Fact]
    public void TryFindBestSale_FindsNothing_WhereNoReachableMarketBuysTheGood()
    {
        var ship = CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 1)]);

        TradeRoutePlanner.TryFindBestSale(Map(), ship, "COPPER_ORE", 1, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFindBestCargoSale_SellsTheGoodThatFetchesMostAfterFuel_First()
    {
        // Slice 6.8: the rule the trading plan sells held cargo by, and the spare-time trip its hold (D36).
        // At A1, 10 FOOD fetch 24,920 and 10 EQUIPMENT 34,990, each less 180 for the fuel.
        var ship = CommandShip(cargo: [new CargoItemModel("FOOD", 10), new CargoItemModel("EQUIPMENT", 10)]);

        TradeRoutePlanner.TryFindBestCargoSale(Map(), ship, mustSell: false, out var cargo, out var sale).Should().BeTrue();

        cargo.Should().Be(new CargoItemModel("EQUIPMENT", 10));
        sale.WaypointSymbol.Should().Be(A1);
    }

    [Fact]
    public void TryFindBestCargoSale_LeavesCargoWhoseSaleDoesntPayForItsFuel_UnlessItMustSell()
    {
        // Only A1, 104 from K85, buys FOOD here: one unit fetches 2,492 there, against 2 FUEL at 18,000 each.
        var map = Map(
            Market(K85, Good("FUEL", "EXCHANGE", 93, 79, 180)),
            Market(A1, Good("FOOD", "IMPORT", 5_028, 2_492, 60), Good("FUEL", "EXCHANGE", 18_000, 76, 180)));
        var ship = CommandShip(cargo: [new CargoItemModel("FOOD", 1)]);

        TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: false, out _, out _).Should().BeFalse();
        TradeRoutePlanner.TryFindBestCargoSale(map, ship, mustSell: true, out var cargo, out var sale).Should().BeTrue();
        (cargo.Symbol, sale.WaypointSymbol).Should().Be(("FOOD", A1));
    }

    [Fact]
    public void AnAsteroidWithoutAMarket_IsNoMarket()
    {
        var map = Map();

        map.MarketWaypoints.Should().BeEquivalentTo([K85, D41, A1]);
        map.SellsFuel(K85).Should().BeTrue();
        map.SellsFuel(Asteroid).Should().BeFalse();
        map.FuelPrice(Asteroid).Should().Be((long)Math.Ceiling((93 + 76 + 90) / 3.0), "where no fuel is sold, the system's average is the estimate");
    }

    /// <summary>A trip on its way to D41 for 15 SHIP_PARTS, to sell at K85 (D80).</summary>
    private static TradeBetweenMarketsGoal ShipPartsTripFromD41()
        => new() { TradeSymbol = "SHIP_PARTS", BuyWaypointSymbol = D41, SellWaypointSymbol = K85, Units = 15, ReservedCredits = 15 * 7_721 };

    /// <summary>D41 selling SHIP_PARTS 15 at a time at 7,721, at MODERATE supply; A1 paying the given price, 40 at a time.</summary>
    private static TradeMarketMap ShipPartsSoldFor(int priceAtA1)
        => Map(
            K85Market(),
            Market(D41, Good("SHIP_PARTS", "EXPORT", 7_721, 3_478, 15), Good("FUEL", "EXCHANGE", 76, 69, 180)),
            Market(A1, Good("SHIP_PARTS", "IMPORT", 16_000, priceAtA1, 40), Good("FUEL", "EXCHANGE", 90, 76, 180)));
}

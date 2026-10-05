using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.5: a trip's profit is what the sell market pays minus what the buy market charges, times
/// the units, minus the fuel; it is lucrative from <c>Trade.MinProfitPerUnit</c> per unit (D14); routes
/// that feed a pricier good's production come first (D15); two traders never share a route. D56: a trip is a
/// full hold, bought in one purchase and sold in one sale, or none; D74: at a seller whose supply is ABUNDANT, what both
/// markets trade at once, in one purchase and one sale, when that fills less.
/// </summary>
public sealed class TradeRoutePlannerTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void Profit_IsTheMarginTimesTheUnits_MinusTheFuelForBothLegs()
    {
        // From K85: 185 to D41 (2 FUEL at D41, 76 each), then 95 to A1 (1 FUEL at A1, 90).
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "MEDICINE", D41, A1, 250_000, out var route).Should().BeTrue();

        route.Units.Should().Be(40, "both markets trade MEDICINE 40 at a time: the command ship's full hold (D56)");
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

        TradeRoutePlanner.TryEvaluate(Map(), ship, "MEDICINE", D41, A1, 250_000, out _).Should().BeFalse();
        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 250_000, out _).Should().BeTrue();
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
    public void WithoutTheCreditsForAFullHold_ThereIsNoRoute_TheFuelKeptBack()
    {
        // D56, "full hold or nothing"; D17: cargo may use the credit reserve, but not the trip's fuel. 40 EQUIPMENT at 3,254
        // and 152 for fuel come to 130,312.
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 130_312, out var route).Should().BeTrue();
        route.Units.Should().Be(40);
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 130_311, out _).Should().BeFalse();
    }

    [Fact]
    public void ARoute_IsOnlyOneWhereOnePurchaseAndOneSaleTakeTheFullHold()
    {
        // D56, asked on 2026-10-03: "The entire goal is to buy full holds in one go, because it makes no sense to buy more
        // times than one", and to sell them in one sale too. D41 sells SHIP_PARTS 15 at a time: a drone's 15-unit hold, not
        // the command ship's 40. A1 takes EQUIPMENT 20 at a time here: neither fills the command ship's hold.
        var map = Map(
            K85Market(),
            D41Market(),
            Market(
                A1,
                Good("EQUIPMENT", "IMPORT", 7_052, 3_499, 20),
                Good("SHIP_PARTS", "IMPORT", 16_000, 8_000, 40),
                Good("FUEL", "EXCHANGE", 90, 76, 180)));

        TradeRoutePlanner.TryEvaluate(map, CommandShip(), "SHIP_PARTS", D41, A1, 1_000_000, out _).Should().BeFalse();
        TradeRoutePlanner.TryEvaluate(map, CommandShip(), "EQUIPMENT", K85, A1, 1_000_000, out _).Should().BeFalse();
        TradeRoutePlanner.TryEvaluate(map, Drone(D41) with { FuelCapacity = 400, FuelCurrent = 400 }, "SHIP_PARTS", D41, A1, 1_000_000, out var drone)
            .Should().BeTrue();
        drone.Units.Should().Be(15);
    }

    [Fact]
    public void AtAnAbundantSeller_ARouteTakesWhatBothMarketsTradeAtOnce_ThoughThatFillsNoHold()
    {
        // D74, asked on 2026-10-04: "either a full hold needs to be obtained, or the supply of the seller needs to be ABUNDANT,
        // in which case a full hold is not necessary", still in one purchase and one sale. D41 sells SHIP_PARTS 15 at a time;
        // the flight to A1 burns 95, one FUEL at A1's 90.
        TradeRoutePlanner.TryEvaluate(ShipPartsMap(), CommandShip(D41), "SHIP_PARTS", D41, A1, 1_000_000, out var route).Should().BeTrue();

        route.Units.Should().Be(15, "one purchase at D41 takes 15, and A1 takes them in one sale");
        route.FuelCost.Should().Be(90);
        route.Profit.Should().Be(((8_000 - 7_721) * 15) - 90);
    }

    [Theory]
    [InlineData(15, 40, 0, 15)]
    [InlineData(15, 6, 0, 6)]
    [InlineData(15, 40, 36, 4)]
    [InlineData(60, 60, 0, 40)]
    public void AtAnAbundantSeller_TheUnitsAreTheSmallestOfTheFreeHoldAndBothTradeVolumes(int atD41, int atA1, int aboard, int units)
    {
        // One purchase and one sale (D74): what A1 takes at once limits the trip as much as what D41 sells at once, and a hold
        // both trade at once is still filled.
        var ship = CommandShip(D41, cargo: aboard == 0 ? null : [new CargoItemModel("COPPER_ORE", aboard)]);

        TradeRoutePlanner.TryEvaluate(ShipPartsMap(atD41: atD41, atA1: atA1), ship, "SHIP_PARTS", D41, A1, 1_000_000, out var route)
            .Should().BeTrue();

        route.Units.Should().Be(units);
    }

    [Theory]
    [InlineData("SCARCE")]
    [InlineData("LIMITED")]
    [InlineData("MODERATE")]
    [InlineData("HIGH")]
    public void AtASellerThatIsntAbundant_ThereIsNoRouteWithoutAFullHold(string supply)
        => TradeRoutePlanner.TryEvaluate(ShipPartsMap(supplyAtD41: supply), CommandShip(D41), "SHIP_PARTS", D41, A1, 1_000_000, out _)
            .Should().BeFalse("D56 holds: 15 at a time fills no 40-unit hold");

    [Fact]
    public void TheBuyersSupply_OpensNoException()
        => TradeRoutePlanner.TryEvaluate(
                ShipPartsMap(supplyAtD41: "MODERATE", supplyAtA1: "ABUNDANT"),
                CommandShip(D41),
                "SHIP_PARTS",
                D41,
                A1,
                1_000_000,
                out _)
            .Should().BeFalse("D74 names the seller's supply, not the buyer's");

    [Fact]
    public void AtAnAbundantSeller_TheCreditsStillPayForAllTheUnits()
    {
        // D56's credits stand: 15 SHIP_PARTS at 7,721 and 90 for fuel come to 115,905.
        TradeRoutePlanner.TryEvaluate(ShipPartsMap(), CommandShip(D41), "SHIP_PARTS", D41, A1, 115_905, out _).Should().BeTrue();
        TradeRoutePlanner.TryEvaluate(ShipPartsMap(), CommandShip(D41), "SHIP_PARTS", D41, A1, 115_904, out _).Should().BeFalse();
    }

    [Fact]
    public void AtAnAbundantSeller_TheMinimumProfitPerUnitStillHolds()
    {
        // D14: 4,095 after fuel over 15 units is 273 a unit.
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 273, NoneHeld)
            .Should().ContainSingle(route => route.TradeSymbol == "SHIP_PARTS" && route.Units == 15);
        TradeRoutePlanner.Rank(ShipPartsMap(), CommandShip(D41), 1_000_000, 274, NoneHeld).Should().BeEmpty();
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
        judged.Single(judgement => judgement.Route.TradeSymbol == "FOOD").Route.Profit.Should().Be((132 * 40) - (2 * 90));
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
    public void Judge_TellsARouteWhoseTradesDontFillTheHold_AtASellerThatIsntAbundant()
        => TradeRoutePlanner.Judge(ShipPartsMap(supplyAtD41: "MODERATE"), CommandShip(D41), 1_000_000, 200, NoneHeld)
            .Should().ContainSingle(judgement => judgement.Route.TradeSymbol == "SHIP_PARTS")
            .Which.Check.Should().Be(TradeRouteCheck.NotFullHold, "D56: 15 at a time fills no 40-unit hold, and D74 needs ABUNDANT");

    [Fact]
    public void Judge_TellsARouteTheCreditsDontPayFor_WithTheUnitsAndTheFuel()
    {
        // 40 EQUIPMENT at 3,254 and 152 for fuel come to 130,312; FOOD, which the credits pay for, earns too little.
        var judged = TradeRoutePlanner.Judge(Map(), CommandShip(), 130_311, 200, NoneHeld);

        var equipment = judged.Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41));
        equipment.Check.Should().Be(TradeRouteCheck.TooFewCredits);
        (equipment.Route.Units, equipment.Route.BuyPrice, equipment.Route.FuelCost).Should().Be((40, 3_254L, 152L));
        equipment.Credits.Should().Be(130_311);
        judged.Single(judgement => judgement.Route.TradeSymbol == "FOOD").Check.Should().Be(TradeRouteCheck.NotLucrative);
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

        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 250_000, out var route).Should().BeTrue();

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
}

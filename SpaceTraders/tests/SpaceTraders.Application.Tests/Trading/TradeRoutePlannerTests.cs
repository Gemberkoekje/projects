using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.5: a trip's profit is what the sell market pays minus what the buy market charges, times
/// the units, minus the fuel; it is lucrative from <c>Trade.MinProfitPerUnit</c> per unit (D14); routes
/// that feed a pricier good's production come first (D15); two traders never share a route.
/// </summary>
public sealed class TradeRoutePlannerTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void Profit_IsTheMarginTimesTheUnits_MinusTheFuelForBothLegs()
    {
        // From K85: 185 to D41 (2 FUEL at D41, 76 each), then 95 to A1 (1 FUEL at A1, 90).
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "MEDICINE", D41, A1, 129_451, out var route).Should().BeTrue();

        route.Units.Should().Be(20, "both markets trade MEDICINE 20 at a time");
        route.BuyPrice.Should().Be(4_867);
        route.SellPrice.Should().Be(5_253);
        route.FuelCost.Should().Be((2 * 76) + 90);
        route.Profit.Should().Be((386 * 20) - 242);
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
                Market("X1-AB-P", Good("GOOD", "EXPORT", 100, 50, 20), Good("FUEL", "EXCHANGE", 100, 90, 180)),
                Market("X1-AB-Q", Good("GOOD", "EXPORT", 100, 50, 20), Good("FUEL", "EXCHANGE", 100, 90, 180)),
                Market("X1-AB-R", Good("GOOD", "IMPORT", 800, 400, 20), Good("FUEL", "EXCHANGE", 100, 90, 180)),
            ],
            MadeFrom);

        var routes = TradeRoutePlanner.Rank(map, CommandShip("X1-AB-P"), 129_451, 0, NoneHeld);

        routes.Select(route => route.BuyWaypointSymbol).Should().Equal("X1-AB-P", "X1-AB-Q");
        routes[0].Profit.Should().Be((300 * 20) - 100);
        routes[1].Profit.Should().Be((300 * 20) - 100 - 200, "the flight to Q is part of the trip");

        static WaypointCacheModel Place(string symbol, int x) => new(symbol, SystemSymbol, "PLANET", x, 0, true, false, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Rank_PutsARouteThatFeedsAPricierGoodFirst_ThoughAnotherEarnsMore()
    {
        // D15: D41 makes SHIP_PARTS (7,721) from EQUIPMENT, so delivering it there grows that production.
        var routes = TradeRoutePlanner.Rank(Map(), CommandShip(), 129_451, 200, NoneHeld);

        routes.Select(route => (route.TradeSymbol, route.BuyWaypointSymbol, route.SellWaypointSymbol)).Should().Equal(
            ("EQUIPMENT", K85, D41),
            ("MEDICINE", D41, A1),
            ("EQUIPMENT", K85, A1));
        routes[0].FeedsTradeSymbol.Should().Be("SHIP_PARTS");
        routes[0].Profit.Should().Be((233 * 20) - (2 * 76));
        routes[1].Profit.Should().BeGreaterThan(routes[0].Profit);
    }

    [Fact]
    public void Rank_WithoutTheProductionChains_GoesByProfitAlone()
    {
        var map = new TradeMarketMap(Waypoints, [K85Market(), D41Market(), A1Market()], new Dictionary<string, IReadOnlyList<string>>());

        var routes = TradeRoutePlanner.Rank(map, CommandShip(), 129_451, 200, NoneHeld);

        routes.Select(route => route.TradeSymbol + " " + route.SellWaypointSymbol).Should().Equal(
            "MEDICINE " + A1,
            "EQUIPMENT " + A1,
            "EQUIPMENT " + D41);
    }

    [Fact]
    public void Rank_LeavesOutRoutesBelowTheMinimumProfitPerUnit()
    {
        // D14: FOOD K85 to A1 earns 5,100 on 40 units, 127 a unit.
        TradeRoutePlanner.Rank(Map(), CommandShip(), 129_451, 200, NoneHeld)
            .Should().NotContain(route => route.TradeSymbol == "FOOD");

        TradeRoutePlanner.Rank(Map(), CommandShip(), 129_451, 0, NoneHeld)
            .Should().ContainSingle(route => route.TradeSymbol == "FOOD")
            .Which.Profit.Should().Be((132 * 40) - (2 * 90));
    }

    [Fact]
    public void Rank_LeavesOutTheRoutesOtherTradersHold()
    {
        var held = new HashSet<string> { TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41) };

        TradeRoutePlanner.Rank(Map(), CommandShip(), 129_451, 200, held)
            .Should().NotContain(route => route.TradeSymbol == "EQUIPMENT" && route.SellWaypointSymbol == D41)
            .And.HaveCount(2);
    }

    [Fact]
    public void Rank_LeavesOutALegLongerThanTheTankHolds()
    {
        // The drone's tank holds 80: every market is further than that from K85.
        TradeRoutePlanner.Rank(Map(), Drone(), 129_451, 0, NoneHeld).Should().BeEmpty();
    }

    [Fact]
    public void AShipNotDockedWhereFuelIsSold_FliesItsFirstLegOnTheFuelAboard()
    {
        // In orbit at K85 with 100 aboard: D41 is 185 away. Buying at K85 is fine: it docks there to
        // buy, and fills its tank before it leaves.
        var ship = CommandShip(status: "IN_ORBIT", fuel: 100);

        TradeRoutePlanner.TryEvaluate(Map(), ship, "MEDICINE", D41, A1, 129_451, out _).Should().BeFalse();
        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 129_451, out _).Should().BeTrue();
    }

    [Fact]
    public void FromBeyondOneTank_TheTripRefuelsOnTheWay()
    {
        // The scout plan ended at J57: from there one tank reaches only I56. Without refuelling stops
        // the command ship found no route at all on 2026-10-02.
        var map = Map([K85Market(), D41Market(), A1Market(), .. FarMarkets()]);
        var ship = CommandShip(J57, fuel: 252);

        var routes = TradeRoutePlanner.Rank(map, ship, 129_451, 200, NoneHeld);

        // J57 to I56 (368: 4 FUEL at 86), I56 to D41 (312: 4 at 76), D41 to A1 (95: 1 at 90).
        var best = routes.Should().NotBeEmpty().And.Subject.First();
        best.TradeSymbol.Should().Be("MEDICINE");
        best.FuelCost.Should().Be((4 * 86) + (4 * 76) + 90);
        best.Profit.Should().Be((386 * 20) - 738);
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
    public void Units_AreLimitedByTheCredits_WithTheFuelKeptBack()
    {
        // D17: cargo may use the credit reserve, but not the trip's fuel.
        TradeRoutePlanner.TryEvaluate(Map(), CommandShip(), "EQUIPMENT", K85, D41, 50_000, out var route).Should().BeTrue();

        route.Units.Should().Be((50_000 - 152) / 3_254);
    }

    [Fact]
    public void Units_AreLimitedByTheFreeHold()
    {
        var ship = CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 35)]);

        TradeRoutePlanner.TryEvaluate(Map(), ship, "EQUIPMENT", K85, D41, 129_451, out var route).Should().BeTrue();

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

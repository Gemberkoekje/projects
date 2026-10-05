using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 2.18 (D76): why a route with a price gap isn't one of a ship's routes, in a sentence, with the figures of the check
/// it fails; and for each good, the route the free traders got furthest with.
/// </summary>
public sealed class TradeRouteJudgementTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void Why_ARouteThatEarnsTooLittle_GivesTheProfitAfterFuel_AndTheMinimum()
    {
        var map = Map();
        var judged = TradeRoutePlanner.Judge(map, CommandShip(), 250_000, 200, NoneHeld);

        Of(judged, "FOOD").Why(map).Should().Be("SHIP-1: 40 units earn 5,100 after 180 for fuel, 127 a unit; a trip must earn 200 a unit (D14).");
        Of(judged, "FUEL").Why(map).Should().Be("SHIP-1: 40 units earn -218 after 338 for fuel, -5 a unit; a trip must earn 200 a unit (D14).");
    }

    [Fact]
    public void Why_ARouteWhoseTradesDontFillTheHold_GivesBothTradeVolumes_AndTheSellersSupply()
    {
        var map = ShipPartsMap(supplyAtD41: "MODERATE");

        Of(TradeRoutePlanner.Judge(map, CommandShip(D41), 1_000_000, 200, NoneHeld), "SHIP_PARTS").Why(map).Should().Be(
            "SHIP-1: one purchase and one sale take 15 of its 40 free units (X1-AB-D41 sells 15 at a time, X1-AB-A1 buys 40); "
            + "less than a full hold needs ABUNDANT supply at X1-AB-D41, which has MODERATE (D56, D74).");
    }

    [Fact]
    public void Why_AShipWithoutRoomInItsHold_SaysSo()
    {
        // Ore the contract wants stays aboard a free trader (D42's exception), and may fill its hold.
        var map = Map();
        var full = CommandShip(cargo: [new CargoItemModel("COPPER_ORE", 40)]);

        Of(TradeRoutePlanner.Judge(map, full, 250_000, 200, NoneHeld), "MEDICINE").Why(map).Should().Be("SHIP-1: no room in its hold.");
    }

    [Fact]
    public void Why_ARouteTheCreditsDontPayFor_GivesWhatItCosts_AndTheCreditsFreeForCargo()
    {
        var map = Map();
        var equipment = TradeRoutePlanner.Judge(map, CommandShip(), 130_311, 200, NoneHeld)
            .Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41));

        equipment.Why(map).Should().Be("SHIP-1: 40 units at 3,254 and 152 for fuel cost 130,312; 130,311 credits are free for cargo (D56).");
    }

    [Fact]
    public void Why_AMarketOutOfReach_NamesTheTankAndWhereTheShipIs()
    {
        var map = Map();
        var judged = TradeRoutePlanner.Judge(map, Drone(), 250_000, 0, NoneHeld);

        Of(judged, "MEDICINE").Why(map).Should().Be(
            "SHIP-3: can't reach X1-AB-D41 from X1-AB-K85 with 80 fuel aboard, refuelling at markets on the way (80-unit tank).");
        judged.Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41)).Why(map).Should().Be(
            "SHIP-3: can't fly on from X1-AB-K85 to X1-AB-D41 with 80 fuel aboard, refuelling at markets on the way (80-unit tank).");
    }

    [Fact]
    public void Why_AMarketOutOfReach_CountsTheFuelAboard_WhereNoTankIsFilledFirst()
    {
        // In orbit at K85 with 100 aboard, the command ship can't fly the 185 to D41 before it docks somewhere with fuel.
        var map = Map();
        var ship = CommandShip(status: "IN_ORBIT", fuel: 100);

        Of(TradeRoutePlanner.Judge(map, ship, 250_000, 200, NoneHeld), "MEDICINE").Why(map).Should().Be(
            "SHIP-1: can't reach X1-AB-D41 from X1-AB-K85 with 100 fuel aboard, refuelling at markets on the way (400-unit tank).");
    }

    [Fact]
    public void Why_ALucrativeRoute_GivesItsProfit()
    {
        var map = Map();
        var equipment = TradeRoutePlanner.Judge(map, CommandShip(), 250_000, 200, NoneHeld)
            .Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41));

        equipment.Why(map).Should().Be("SHIP-1: lucrative, 9,168 after fuel, 229 a unit.");
    }

    [Fact]
    public void FurthestPerGood_IsTheRouteAndShipThatPassedTheMostChecks()
    {
        // D41 sells SHIP_PARTS 15 at a time, its supply MODERATE: the command ship's 40-unit hold takes no full hold there
        // (D56), and a shuttle's 15-unit hold does, but earns 273 a unit after fuel, under the 300 asked (D14).
        var map = ShipPartsMap(supplyAtD41: "MODERATE");
        var shuttle = Drone(D41, "SHIP-5") with { FuelCapacity = 400, FuelCurrent = 400 };
        var judged = TradeRoutePlanner.Judge(map, CommandShip(D41), 1_000_000, 300, NoneHeld)
            .Concat(TradeRoutePlanner.Judge(map, shuttle, 1_000_000, 300, NoneHeld));

        var parts = TradeRouteJudgement.FurthestPerGood(judged).Should().ContainSingle(judgement => judgement.Route.TradeSymbol == "SHIP_PARTS").Subject;

        (parts.Ship.Symbol, parts.Check).Should().Be(("SHIP-5", TradeRouteCheck.NotLucrative));
    }

    [Fact]
    public void FurthestPerGood_AmongRoutesThatGotAsFar_IsTheOneWithTheLargestPriceGap()
    {
        // Neither EQUIPMENT route is paid for: A1 pays 245 above K85's price, D41 233.
        var judged = TradeRoutePlanner.Judge(Map(), CommandShip(), 1_000, 200, NoneHeld);

        var equipment = TradeRouteJudgement.FurthestPerGood(judged).Single(judgement => judgement.Route.TradeSymbol == "EQUIPMENT");

        (equipment.Route.SellWaypointSymbol, equipment.Check).Should().Be((A1, TradeRouteCheck.TooFewCredits));
    }

    [Fact]
    public void FurthestPerGood_ListsTheFurthestFirst_ThenByGood()
        => TradeRouteJudgement.FurthestPerGood(TradeRoutePlanner.Judge(Map(), CommandShip(), 130_311, 200, NoneHeld))
            .Select(judgement => (judgement.Route.TradeSymbol, judgement.Check))
            .Should().Equal(
                ("FOOD", TradeRouteCheck.NotLucrative),
                ("FUEL", TradeRouteCheck.NotLucrative),
                ("EQUIPMENT", TradeRouteCheck.TooFewCredits),
                ("MEDICINE", TradeRouteCheck.TooFewCredits));

    private static TradeRouteJudgement Of(IEnumerable<TradeRouteJudgement> judged, string tradeSymbol)
        => judged.Single(judgement => judgement.Route.TradeSymbol == tradeSymbol);
}

using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 2.18 (D76): why a route with a price gap isn't one of a ship's routes, in a sentence, with the figures of the check
/// it fails; and for each good, the route the free traders got furthest with. Since D79 a route carries as many units as each
/// earn the minimum, so it fails on its first unit, or on the whole trip after fuel.
/// </summary>
public sealed class TradeRouteJudgementTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void Why_ARouteWhoseFirstUnitEarnsTooLittle_GivesWhatAUnitEarns_AndTheMinimum()
    {
        var map = Map();
        var judged = TradeRoutePlanner.Judge(map, CommandShip(), 250_000, 200, NoneHeld);

        Of(judged, "FOOD").Why(map).Should().Be("SHIP-1: a unit bought at 2,360 and sold at 2,492 earns 132 before fuel; each must earn 200 (D14, D79).");
        Of(judged, "FUEL").Why(map).Should().Be("SHIP-1: a unit bought at 76 and sold at 79 earns 3 before fuel; each must earn 200 (D14, D79).");
    }

    [Fact]
    public void Why_ATripWhoseUnitsEarnTooLittleAfterFuel_GivesTheProfitAfterFuel_AndTheMinimum()
    {
        // D14 on the whole trip: each of the 15 SHIP_PARTS earns 279, the trip 4,095 after its 90 for fuel.
        var map = ShipPartsMap();

        Of(TradeRoutePlanner.Judge(map, CommandShip(D41), 1_000_000, 274, NoneHeld), "SHIP_PARTS").Why(map).Should().Be(
            "SHIP-1: 15 units earn 4,095 after 90 for fuel, 273 a unit; a trip must earn 274 a unit (D14).");
    }

    [Fact]
    public void Why_AMarketThatNamesNoTradeVolume_SaysSo()
    {
        var map = ShipPartsMap(atA1: 0);

        Of(TradeRoutePlanner.Judge(map, CommandShip(D41), 1_000_000, 200, NoneHeld), "SHIP_PARTS").Why(map).Should().Be(
            "SHIP-1: X1-AB-D41 or X1-AB-A1 names no price or trade volume for SHIP_PARTS.");
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
    public void Why_ARouteTheCreditsDontPayAUnitOf_GivesWhatItCosts_AndTheCreditsFreeForCargo()
    {
        var map = Map();
        var equipment = TradeRoutePlanner.Judge(map, CommandShip(), 3_405, 200, NoneHeld)
            .Single(judgement => judgement.Route.Key == TradeRoutePlanner.RouteKey("EQUIPMENT", K85, D41));

        equipment.Why(map).Should().Be("SHIP-1: a unit at 3,254 and 152 for the trip's fuel cost 3,406; 3,405 credits are free for cargo.");
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

        equipment.Why(map).Should().Be("SHIP-1: lucrative, 40 units for 9,168 after fuel, 229 a unit.");
    }

    [Fact]
    public void FurthestPerGood_IsTheRouteAndShipThatPassedTheMostChecks()
    {
        // SHIP-1, whose hold is full, has no room for SHIP_PARTS; SHIP-5's empty hold has, but its first unit earns 279,
        // under the 300 asked (D14, D79).
        var map = ShipPartsMap();
        var full = CommandShip(D41, cargo: [new CargoItemModel("COPPER_ORE", 40)]);
        var empty = CommandShip(D41, symbol: "SHIP-5");
        var judged = TradeRoutePlanner.Judge(map, full, 1_000_000, 300, NoneHeld)
            .Concat(TradeRoutePlanner.Judge(map, empty, 1_000_000, 300, NoneHeld));

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
        => TradeRouteJudgement.FurthestPerGood(TradeRoutePlanner.Judge(Map(), CommandShip(), 3_000, 200, NoneHeld))
            .Select(judgement => (judgement.Route.TradeSymbol, judgement.Check))
            .Should().Equal(
                ("FOOD", TradeRouteCheck.NotLucrative),
                ("FUEL", TradeRouteCheck.NotLucrative),
                ("EQUIPMENT", TradeRouteCheck.TooFewCredits),
                ("MEDICINE", TradeRouteCheck.TooFewCredits));

    private static TradeRouteJudgement Of(IEnumerable<TradeRouteJudgement> judged, string tradeSymbol)
        => judged.Single(judgement => judgement.Route.TradeSymbol == tradeSymbol);
}

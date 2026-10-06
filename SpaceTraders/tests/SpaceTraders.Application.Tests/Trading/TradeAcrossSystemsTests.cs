using FluentAssertions;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Trading.TradeAcrossFixture;
using static SpaceTraders.Application.Tests.Trading.TradeFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// PLAN.md slice 6.29 (D95, D96, D101): the trading plan's routes reach the systems around, through the built gates. A route
/// buys within <c>Trade.MaxHaulDistance</c> jumps of the ship's system and sells within that many of the buy market's; its
/// jumps' antimatter is a cost, and their cooldowns count in its time where they hold the ship; its rate does the choosing,
/// so a far route goes first only when it earns more an hour. Prices more than <c>Trade.MaxPriceAgeMinutes</c> old choose no
/// route, at home too.
/// </summary>
public sealed class TradeAcrossSystemsTests
{
    private static readonly IReadOnlySet<string> NoneHeld = new HashSet<string>();

    [Fact]
    public void ADistance_IsMeasuredWithinASystemOnly()
    {
        var map = AcrossMap();

        map.TryGetDistance(K85, AbGate, out var near).Should().BeTrue();
        near.Should().BeApproximately(82, 0.001);
        map.TryGetDistance(AbGate, CdGate, out _).Should().BeFalse("between systems a ship jumps");
        map.SameSystem(CdGate, CdMarket).Should().BeTrue();
    }

    [Fact]
    public void AFlightToAnotherSystem_FliesToTheGate_Jumps_AndFliesOn()
    {
        TradeRoutePlanner.TryPlanFlight(AcrossMap(), K85, CdMarket, 400, 400, out var flight).Should().BeTrue();

        flight.Stops.Should().Equal(AbGate, CdGate, CdMarket);
        flight.Jumps.Should().Equal(new GateJump(AbGate, CdGate));
        flight.AntimatterCost.Should().Be(Antimatter, "one ANTIMATTER at the gate it jumps from");
        flight.FuelCost.Should().Be(90 + 80, "82 to the gate and 50 on, each a market unit of FUEL where it ends");
        flight.FuelLeft.Should().Be(350, "X1-CD's gate sells fuel, so the tank is full when it flies on");
    }

    [Fact]
    public void WithoutJumps_TheMapReachesItsOwnSystemOnly()
    {
        var home = AcrossMap().WithoutJumps();

        TradeRoutePlanner.TryPlanFlight(home, K85, CdMarket, 400, 400, out _).Should().BeFalse();
        TradeRoutePlanner.TryPlanFlight(home, K85, A1, 400, 400, out _).Should().BeTrue();
        TradeRoutePlanner.Rank(home, CommandShip(), 250_000, 200, NoneHeld).Should().OnlyContain(route => route.Jumps == 0);
    }

    [Fact]
    public void ARouteAbroad_GoesFirst_WhenItEarnsMoreAnHour_AfterItsAntimatter()
    {
        // Asked on 2026-10-06: "I'd like to expand the trade system so other systems actually get considered and used." X1-CD
        // pays 6,000 for EQUIPMENT, A1 3,499. The trip abroad: buy 40 at K85 (3,254), 82 to the gate, one jump, 50 on. It earns
        // 104,670 after 170 for fuel and 5,000 for antimatter in about 7 minutes, against A1's 9,620 in about 5.
        var routes = TradeRoutePlanner.Rank(AcrossMap(), CommandShip(), 250_000, 200, NoneHeld);

        var abroad = routes[0];
        (abroad.TradeSymbol, abroad.BuyWaypointSymbol, abroad.SellWaypointSymbol).Should().Be(("EQUIPMENT", K85, CdMarket));
        abroad.Units.Should().Be(40);
        abroad.Jumps.Should().Be(1);
        abroad.AntimatterCost.Should().Be(Antimatter);
        abroad.FuelCost.Should().Be(170);
        abroad.Profit.Should().Be((40 * (6_000 - 3_254)) - 170 - Antimatter);
        abroad.Seconds.Should().BeApproximately(
            TripTime.StopSeconds + (15 + (82 * 25 / 9.0)) + TripTime.StopSeconds + TripTime.StopSeconds + (15 + (50 * 25 / 9.0)) + TripTime.StopSeconds,
            0.001,
            "a stop to buy, the flight to the gate, the jump, and the flight on; the jump's cooldown holds no flight");
        routes.Should().Contain(route => route.SellWaypointSymbol == A1 && route.TradeSymbol == "EQUIPMENT" && route.Profit == 9_620);
    }

    [Fact]
    public void ARouteAbroad_ThatEarnsLessAnHour_ComesAfterTheRouteAtHome()
    {
        // D95: the rate does the choosing. At 3,700 the trip abroad still earns more a trip (12,670 against 9,620), but its
        // jump's antimatter and the longer way make it less an hour: about 104,000 against 107,000.
        var routes = TradeRoutePlanner.Rank(AcrossMap(cdEquipmentPrice: 3_700), CommandShip(), 250_000, 200, NoneHeld)
            .Where(route => route.TradeSymbol == "EQUIPMENT")
            .ToList();

        routes.Select(route => route.SellWaypointSymbol).Take(2).Should().Equal(A1, CdMarket);
        routes[1].Profit.Should().BeGreaterThan(routes[0].Profit);
        routes[1].CreditsPerHour.Should().BeLessThan(routes[0].CreditsPerHour);
    }

    [Fact]
    public void AMarketWhosePricesAreTooOld_ChoosesNoRoute()
    {
        // D96: "prices over 30 minutes old don't count", at home too.
        var routes = TradeRoutePlanner.Rank(AcrossMap(staleMarkets: [CdMarket, A1]), CommandShip(), 250_000, 200, NoneHeld);

        routes.Should().NotBeEmpty();
        routes.Should().NotContain(route => route.SellWaypointSymbol == CdMarket || route.SellWaypointSymbol == A1 || route.BuyWaypointSymbol == A1);
        TradeRoutePlanner.Judge(AcrossMap(staleMarkets: [CdMarket]), CommandShip(), 250_000, 200, NoneHeld)
            .Should().NotContain(judgement => judgement.Route.SellWaypointSymbol == CdMarket, "a stale market isn't judged either");
    }

    [Fact]
    public void TheBuyMarketCountsWithinTheReachOfTheShip_TheSellMarketWithinTheReachOfTheBuyMarket()
    {
        // D96, read as: with a reach of 1, X1-EF, two jumps from the ship, is out of reach for EQUIPMENT from K85, though it
        // pays 9,000; PLASTICS bought in X1-CD, one jump away, sell in X1-EF, one jump on from there.
        var routes = TradeRoutePlanner.Rank(AcrossMap(efEquipmentPrice: 9_000, maxJumps: 1), CommandShip(), 250_000, 200, NoneHeld);

        routes.Should().NotContain(route => route.TradeSymbol == "EQUIPMENT" && route.SellWaypointSymbol == EfMarket);
        var plastics = routes.Should().ContainSingle(route => route.TradeSymbol == "PLASTICS").Subject;
        (plastics.BuyWaypointSymbol, plastics.SellWaypointSymbol, plastics.Jumps, plastics.AntimatterCost).Should().Be((CdMarket, EfMarket, 2, 2 * Antimatter));

        TradeRoutePlanner.Rank(AcrossMap(efEquipmentPrice: 9_000, maxJumps: 2), CommandShip(), 250_000, 200, NoneHeld)
            .Should().Contain(route => route.TradeSymbol == "EQUIPMENT" && route.SellWaypointSymbol == EfMarket && route.Jumps == 2);
    }

    [Fact]
    public void ATripThroughTheGates_KeepsTheCreditFloorBesidesItsAntimatter()
    {
        // D63: a jump leaves the credit floor (60,000), so a ship with its cargo aboard can always jump on: of 150,000, the trip
        // abroad buys what 150,000 - 170 - 5,000 - 60,000 pays for, 26 at 3,254. At home all 40 are paid for.
        var routes = TradeRoutePlanner.Rank(AcrossMap(), CommandShip(), 150_000, 200, NoneHeld);

        routes.Single(route => route.SellWaypointSymbol == CdMarket).Units.Should().Be(26);
        routes.Single(route => route.SellWaypointSymbol == A1 && route.TradeSymbol == "EQUIPMENT").Units.Should().Be(40);
    }

    [Fact]
    public void ASecondJump_WaitsOutTheCooldownOfTheFirst()
    {
        // From K85 to X1-EF's market: two jumps. The ship lands at X1-CD's gate by the first, and the second waits for its
        // cooldown there; once in X1-EF it flies on at once.
        TradeRoutePlanner.TryPlanFlight(AcrossMap(), K85, EfMarket, 400, 400, out var flight).Should().BeTrue();
        flight.Stops.Should().Equal(AbGate, CdGate, EfGate, EfMarket);

        var seconds = TripTime.TradeSeconds(AcrossMap(), K85, [], K85, flight.Stops, 9);

        var toGate = TripTime.StopSeconds + (15 + (82 * 25 / 9.0));
        var firstJump = toGate + TripTime.StopSeconds;
        seconds.Should().BeApproximately(
            TripTime.StopSeconds + Math.Max(firstJump, toGate + Cooldown) + TripTime.StopSeconds + (15 + (50 * 25 / 9.0)) + TripTime.StopSeconds,
            0.001);
    }

    [Fact]
    public void AJump_WaitsForWhatIsLeftOfTheShipsOwnCooldown()
    {
        // A trader that has just jumped in takes a route back through the gate: the jump waits until its cooldown is over.
        var map = AcrossMap();
        var ship = CommandShip() with { CooldownExpiresAt = Now.AddSeconds(600) };

        var routes = TradeRoutePlanner.Rank(map, ship, 250_000, 200, NoneHeld);
        var rested = TradeRoutePlanner.Rank(map, CommandShip(), 250_000, 200, NoneHeld);

        var abroad = routes.Single(route => route.SellWaypointSymbol == CdMarket);
        var abroadRested = rested.Single(route => route.SellWaypointSymbol == CdMarket);
        abroad.Seconds.Should().BeApproximately(600 + (2 * TripTime.StopSeconds) + 15 + (50 * 25 / 9.0), 0.001);
        abroad.Seconds.Should().BeGreaterThan(abroadRested.Seconds);
        routes.Single(route => route.SellWaypointSymbol == A1 && route.TradeSymbol == "EQUIPMENT").Seconds
            .Should().Be(rested.Single(route => route.SellWaypointSymbol == A1 && route.TradeSymbol == "EQUIPMENT").Seconds, "a flight doesn't wait for it");
    }

    [Fact]
    public void AHeldCargoSale_CountsTheAntimatterToo()
    {
        // 40 EQUIPMENT aboard: X1-CD pays 6,000, after 170 for fuel and 5,000 for antimatter; A1 3,499.
        var ship = CommandShip(cargo: [new CargoItemModel("EQUIPMENT", 40)]);

        TradeRoutePlanner.TryFindBestSale(AcrossMap(), ship, "EQUIPMENT", 40, out var sale).Should().BeTrue();

        sale.WaypointSymbol.Should().Be(CdMarket);
        sale.NetRevenue.Should().Be((40 * 6_000) - 170 - Antimatter);
    }

    [Fact]
    public void TheGates_FindTheFewestJumps_WithinTheReach_AndEstimateEachCooldownByTheSystemsDistance()
    {
        var gates = Gates(maxJumps: 1);

        gates.TryFindWay(Ab, Cd, out var one).Should().BeTrue();
        one.Should().Equal(new GateJump(AbGate, CdGate));
        gates.TryFindWay(Ab, Ef, out _).Should().BeFalse("two jumps, beyond the reach of 1");
        Gates(maxJumps: 2).TryFindWay(Ab, Ef, out var two).Should().BeTrue();
        two.Should().HaveCount(2);

        // Fitted to SPECTER-1's twelve jumps of 2026-10-06: X1-AA31 to X1-PX46, 1,891 apart, left 603 seconds.
        gates.CooldownSeconds(Ab, Cd).Should().BeApproximately(17 + 311, 0.001);
        gates.CooldownSeconds(Ab, "X1-UNKNOWN").Should().Be(TradeGates.CooldownBaseSeconds);
        gates.AntimatterAt(CdGate).Should().Be(Antimatter);
        gates.AntimatterAt("X1-UNKNOWN-G").Should().Be(Antimatter, "where it was never seen, the average of the gates that were");
    }
}

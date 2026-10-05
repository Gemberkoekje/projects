using FluentAssertions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Tests.Siphoning;
using SpaceTraders.Application.Trading;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.19 (D84): the leg a ship flies next (<see cref="TradeRoutePlanner.TryPlanNextLeg"/>). Asked on 2026-10-05: "I'd
/// like a ship to burn if they can reach the destination with double fuel consumption, but cruise if they cannot", and
/// "can we optimize the routing for a location where a combination of cruising and drifting is faster than just
/// drifting?" No leg lands a ship with an empty tank where no fuel is sold: "A ship can technically land anywhere with 1
/// fuel and then drift to a fuel station". Mostly in X1-DC53 as it was on 2026-10-02 (<see cref="MiningFixture"/>), with
/// its drones' 80-unit tanks.
/// </summary>
public sealed class FlightLegTests
{
    [Fact]
    public void ALegIntoAMarketThatSellsFuel_Burns_WhenTheTankHoldsTwiceItsFuel()
    {
        // XB5C, which sells fuel, is 19 from H51: burning takes 38 of the 80 a docked drone leaves with.
        TradeRoutePlanner.TryPlanNextLeg(Map(), Drone(), XB5C, string.Empty, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg(XB5C, "BURN"));
    }

    [Fact]
    public void ALeg_Cruises_WhenTheTankHoldsLessThanTwiceItsFuel()
    {
        // F49 is 52 from H51: burning would take 104.
        TradeRoutePlanner.TryPlanNextLeg(Map(), Drone(), F49, string.Empty, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg(F49, "CRUISE"));
    }

    [Fact]
    public void ALegIntoAnAsteroid_Burns_WhenWhatIsLeftStillCruisesOnward()
    {
        // B14 is 25 from B7: burning takes 50 of a full tank and leaves 30, enough to cruise the ore back to B7.
        TradeRoutePlanner.TryPlanNextLeg(Map(), Drone(waypoint: B7), B14, B7, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg(B14, "BURN"));
    }

    [Fact]
    public void ALegIntoAnAsteroid_Cruises_WhenBurningWouldLeaveTooLittleToCruiseOnward()
    {
        // In orbit at B7 with 60 aboard: burning to B14 would leave 10, short of the 25 back; cruising leaves 35.
        var drone = Drone(waypoint: B7, status: "IN_ORBIT") with { FuelCurrent = 60 };

        TradeRoutePlanner.TryPlanNextLeg(Map(), drone, B14, B7, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg(B14, "CRUISE"));
    }

    [Fact]
    public void ALegIntoAnAsteroid_Cruises_WhenBurningWouldCostAnExtraFuelStopOnward()
    {
        // From M, the asteroid A is 30 away and the onward market O 70 beyond it. Cruising to A leaves 70 of a 100-unit tank,
        // enough for O; burning leaves 40, which only takes the ship back to M to refuel first.
        var map = Line(("M", 0, true), ("A", 30, false), ("O", 100, true));

        TradeRoutePlanner.TryPlanNextLeg(map, ShipAt("M", 100), "A", "O", out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg("A", "CRUISE"));
    }

    [Fact]
    public void ALegIntoAnAsteroid_WithNowhereToGoNext_Burns_OnlyWhenAMarketThatSellsFuelIsInCruiseReachAfterwards()
    {
        // B58: a ship isn't left where it can't cruise to fuel. Burning the 20 to A leaves 40, and M is 20 back; burning the
        // 30 to B would leave 20, short of the 30 back to M.
        var map = Line(("M", 0, true), ("A", 20, false), ("B", 30, false));

        TradeRoutePlanner.TryPlanNextLeg(map, ShipAt("M", 80), "A", string.Empty, out var toA).Should().BeTrue();
        TradeRoutePlanner.TryPlanNextLeg(map, ShipAt("M", 80), "B", string.Empty, out var toB).Should().BeTrue();

        toA.Should().Be(new FlightLeg("A", "BURN"));
        toB.Should().Be(new FlightLeg("B", "CRUISE"));
    }

    [Fact]
    public void OutOfCruiseReach_TheFastestWay_CruisesAsFarAsItCan_AndDriftsTheRest()
    {
        // No chain of fuel markets 80 apart leads from H51 to B7, 310 away. Cruising the 52 to F49 and drifting the 274 from
        // there beats drifting the 310 straight there.
        TradeRoutePlanner.TryPlanMixedFlight(Map(), H51, B7, 80, 80, out var legs).Should().BeTrue();
        TradeRoutePlanner.TryPlanNextLeg(Map(), Drone(), B7, string.Empty, out var leg).Should().BeTrue();

        legs.Should().Equal(new FlightLeg(F49, "CRUISE"), new FlightLeg(B7, "DRIFT"));
        leg.Should().Be(new FlightLeg(F49, "CRUISE"));
    }

    [Fact]
    public void OutOfCruiseReach_TheFastestWay_DriftsStraightThere_WhenNothingIsFaster()
    {
        // From F49, nothing on the way shortens the drift to B7.
        TradeRoutePlanner.TryPlanMixedFlight(Map(), F49, B7, 80, 80, out var legs).Should().BeTrue();

        legs.Should().Equal(new FlightLeg(B7, "DRIFT"));
    }

    [Fact]
    public void OutOfCruiseReach_ACruiseLegIntoAMarketThatSellsFuel_Burns_WhenTheTankHoldsTwiceItsFuel()
    {
        // From C39 the fastest way to F48 cruises the 39 to C40 and the 70 to E47, and drifts the 126 from there. C40 sells
        // fuel, so the tank fills again there, and it holds twice the 39: the first leg burns.
        var map = SiphonFixture.Map();

        TradeRoutePlanner.TryPlanMixedFlight(map, SiphonFixture.C39, SiphonFixture.F48, 80, 80, out var legs).Should().BeTrue();
        TradeRoutePlanner.TryPlanNextLeg(map, SiphonFixture.SiphonDrone(), SiphonFixture.F48, string.Empty, out var leg).Should().BeTrue();

        legs.Should().Equal(
            new FlightLeg(SiphonFixture.C40, "CRUISE"),
            new FlightLeg(SiphonFixture.E47, "CRUISE"),
            new FlightLeg(SiphonFixture.F48, "DRIFT"));
        leg.Should().Be(new FlightLeg(SiphonFixture.C40, "BURN"));
    }

    [Fact]
    public void NoLeg_LandsTheShipWithAnEmptyTank_WhereNoFuelIsSold()
    {
        // The asteroid A is 80 from M, all the tank holds: cruising there would leave nothing, and even a drift takes 1, so
        // the ship could never leave. It drifts there instead, and lands with 79.
        var map = Line(("M", 0, true), ("A", 80, false));

        TradeRoutePlanner.TryPlanNextLeg(map, ShipAt("M", 80), "A", string.Empty, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg("A", "DRIFT"));
    }

    [Fact]
    public void ALeg_MayEmptyTheTank_WhereFuelIsSold()
    {
        var map = Line(("M", 0, true), ("N", 80, true));

        TradeRoutePlanner.TryPlanNextLeg(map, ShipAt("M", 80), "N", string.Empty, out var leg).Should().BeTrue();

        leg.Should().Be(new FlightLeg("N", "CRUISE"));
    }

    [Fact]
    public void TheFastestWay_CruisesToAnAsteroid_DriftsOnWithTheLastFuel_AndDriftsBackToFuel()
    {
        // The example of 2026-10-05: "it can cruise to an asteroid with 2 fuel left, drift to the correct asteroid with 1
        // fuel left, then drift back to a fuel station with 0 fuel and refuel."
        var map = Line(("M", 0, true), ("X", 78, false), ("Y", 90, false));

        TradeRoutePlanner.TryPlanMixedFlight(map, "M", "Y", 80, 80, out var there).Should().BeTrue();
        TradeRoutePlanner.TryPlanMixedFlight(map, "Y", "M", 1, 80, out var back).Should().BeTrue();

        there.Should().Equal(new FlightLeg("X", "CRUISE"), new FlightLeg("Y", "DRIFT"));
        back.Should().Equal(new FlightLeg("M", "DRIFT"));
    }

    [Fact]
    public void TheFastestWay_NeverDriftsOnWithTheLastFuel_WhereNoFuelIsSold()
    {
        // With X 79 from M, cruising there leaves 1, and drifting on to Y would land with none. The ship drifts to X on a
        // full tank instead, and cruises the 11 on.
        var map = Line(("M", 0, true), ("X", 79, false), ("Y", 90, false));

        TradeRoutePlanner.TryPlanMixedFlight(map, "M", "Y", 80, 80, out var legs).Should().BeTrue();

        legs.Should().Equal(new FlightLeg("X", "DRIFT"), new FlightLeg("Y", "CRUISE"));
    }

    [Fact]
    public void WithoutFuel_NoWayIsFound()
        => TradeRoutePlanner.TryPlanMixedFlight(Map(), B13, B7, 0, 80, out _).Should().BeFalse();

    /// <summary>A system on a line: each waypoint at its distance from the first, selling fuel or not.</summary>
    private static TradeMarketMap Line(params (string Symbol, int X, bool SellsFuel)[] waypoints)
        => new(
            [
                .. waypoints.Select(waypoint => new WaypointCacheModel(
                    waypoint.Symbol,
                    SystemSymbol,
                    waypoint.SellsFuel ? "FUEL_STATION" : "ASTEROID",
                    waypoint.X,
                    0,
                    waypoint.SellsFuel,
                    false,
                    DateTimeOffset.UnixEpoch,
                    TraitsJson: waypoint.SellsFuel ? """[{"symbol":"MARKETPLACE"}]""" : "[]")),
            ],
            [
                .. waypoints.Where(waypoint => waypoint.SellsFuel).Select(waypoint => new MarketSnapshot(
                    waypoint.Symbol,
                    SystemSymbol,
                    [new TradeGoodSnapshot("FUEL", "EXCHANGE", 72, 68, 180, "MODERATE")],
                    [],
                    [],
                    ["FUEL"])),
            ],
            new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>A ship docked at a waypoint of <see cref="Line"/>, with a full tank.</summary>
    private static ShipModel ShipAt(string waypoint, int fuelCapacity)
        => new("SHIP-9", SystemSymbol, waypoint, "DOCKED", "CRUISE", fuelCapacity, fuelCapacity);
}

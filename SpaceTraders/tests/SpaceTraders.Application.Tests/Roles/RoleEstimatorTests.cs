using FluentAssertions;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Tests.Mining;
using SpaceTraders.Application.Tests.Trading;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.9 (D38): what a role would earn a ship per hour, from the trips its plan would offer: what a trip earns after
/// fuel, with the production chains' share (D39), over how long it takes.
/// </summary>
public sealed class RoleEstimatorTests
{
    [Fact]
    public void AFlight_TakesWhatTheApiReckonsForCruise_LegByLeg()
    {
        // K85 to D41 is 185.4 (185), D41 to A1 94.9 (95): 15 + 185 x 25 / 36, then 15 + 95 x 25 / 36.
        var map = TradeFixture.Map();

        RoleEstimator.FlightSeconds(map, TradeFixture.K85, [TradeFixture.D41, TradeFixture.A1], 36)
            .Should().BeApproximately(15 + (185 * 25 / 36.0) + 15 + (95 * 25 / 36.0), 0.001);
        RoleEstimator.FlightSeconds(map, TradeFixture.K85, [], 36).Should().Be(0);
    }

    [Fact]
    public void ATradeTrip_EarnsItsProfitAfterFuel_InTheTimeItsFlightsAndStopsTake()
    {
        // The command ship at K85, docked: EQUIPMENT to D41 is the best route there (6.5's dry run).
        var map = TradeFixture.Map();
        var ship = TradeFixture.CommandShip() with { EngineJson = """{"speed":36}""" };
        var route = TradeRoutePlanner.Rank(map, ship, 129_451, 200, new HashSet<string>())
            .Single(candidate => candidate.TradeSymbol == "EQUIPMENT" && candidate.SellWaypointSymbol == TradeFixture.D41);

        var option = RoleEstimator.Options(Context(map), ship, FleetRole.Trade, 20)
            .Single(candidate => candidate.JobKey == "trade|" + route.Key);

        // Bought where it is (one stop), flown to D41 (one stop).
        option.Credits.Should().Be(route.Profit);
        option.Seconds.Should().BeApproximately(15 + (185 * 25 / 36.0) + (2 * RoleEstimator.StopSeconds), 0.001);
        option.CreditsPerHour.Should().BeApproximately(route.Profit * 3_600 / option.Seconds, 0.001);
    }

    [Fact]
    public void AMiningTrip_FillsTheHoldWithItsOre_AtTheShipsRate_TimesTheOresShare()
    {
        // A drone at XB5C mines silicon for F49 without a survey: one of XB5C's six ores comes up an extraction. At 3 units
        // every 70 seconds, the 15-unit hold takes 30 extractions.
        var map = MiningFixture.Map();
        var drone = MiningFixture.Drone(waypoint: MiningFixture.XB5C);

        var option = RoleEstimator.Options(Context(map), drone, FleetRole.Mine, 20)
            .Single(candidate => candidate.JobKey == "mine|" + MiningPlanner.OpportunityKey(MiningFixture.F49, "SILICON_CRYSTALS"));

        var flight = RoleEstimator.FlightSeconds(map, MiningFixture.XB5C, [MiningFixture.F49], RoleEstimator.DefaultEngineSpeed);
        option.Seconds.Should().BeApproximately((30 * (70 + (RoleEstimator.TickSeconds / 2))) + flight + RoleEstimator.StopSeconds, 0.001);
        option.Credits.Should().Be((15 * 49) - (long)Math.Ceiling(63 / 100.0) * 82);
    }

    [Fact]
    public void AFasterMiner_EarnsMorePerHour_ForTheSameTrip()
    {
        var map = MiningFixture.Map();
        var rates = new GatheringRates();
        rates.Record("SHIP-3", GatheringKind.Mining, 6, 70);
        var slow = Best(Context(map), MiningFixture.Drone("SHIP-4", MiningFixture.XB5C), FleetRole.Mine);
        var fast = Best(Context(map, rates: rates), MiningFixture.Drone("SHIP-3", MiningFixture.XB5C), FleetRole.Mine);

        fast.CreditsPerHour.Should().BeGreaterThan(slow.CreditsPerHour * 1.5);
    }

    [Fact]
    public void TheProductionChains_AddToWhatAnOreFetches()
    {
        // H51 is LIMITED in copper and makes COPPER from it, at 400 against copper ore's 138.
        var markets = MiningFixture.Markets().Select(market => market.WaypointSymbol == MiningFixture.H51
            ? market with { TradeGoods = [.. market.TradeGoods, MiningFixture.Good("COPPER", "EXPORT", 400, 200, 60, "MODERATE")] }
            : market).ToArray();
        var map = new TradeMarketMap(MiningFixture.Waypoints, markets, new Dictionary<string, IReadOnlyList<string>> { ["COPPER"] = ["COPPER_ORE"] });
        var drone = MiningFixture.Drone(waypoint: MiningFixture.XB5C);
        var key = "mine|" + MiningPlanner.OpportunityKey(MiningFixture.H51, "COPPER_ORE");

        var without = RoleEstimator.Options(Context(map, share: 0), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);
        var with = RoleEstimator.Options(Context(map, share: 0.5), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);

        // Half of the 262 difference, three quarters of it at LIMITED, on each of the 15 units.
        (with.Credits - without.Credits).Should().Be((long)Math.Round(15 * 0.5 * 0.75 * (400 - 138)));
    }

    [Fact]
    public void ARoleTheShipCantTake_AndSurveying_OfferNothing()
    {
        var map = MiningFixture.Map();

        RoleEstimator.Options(Context(map), MiningFixture.Drone(), FleetRole.Siphon, 20).Should().BeEmpty();
        RoleEstimator.Options(Context(map), MiningFixture.CommandShip(), FleetRole.Survey, 20).Should().BeEmpty();
    }

    private static RoleOption Best(RoleContext context, ShipModel ship, FleetRole role)
        => RoleEstimator.Options(context, ship, role, 1).Single();

    private static RoleContext Context(TradeMarketMap map, double share = 0, GatheringRates? rates = null)
        => new(new MiningContext(map, [], 129_451, MiningFixture.Now), 200, 0, new ChainValues(map, share), rates ?? new GatheringRates());
}

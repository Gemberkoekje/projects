using FluentAssertions;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Tests.Mining;
using SpaceTraders.Application.Tests.Siphoning;
using SpaceTraders.Application.Tests.Trading;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>
/// Slice 6.9 (D38): what a role would earn a ship per hour, from the trips its plan would offer: what a trip earns after
/// fuel, with the production chains' share (D39) at most as much again as the goods earn (D49), over how long it takes.
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
    public void ADrift_TakesTenTimesWhatCruiseDoes()
    {
        // Slice 6.10c (D45): 15 seconds plus the distance times 250 over the engine's speed. XB5C to B7 is 327.9 (328).
        RoleEstimator.DriftSeconds(MiningFixture.Map(), MiningFixture.XB5C, MiningFixture.B7, 9).Should().BeApproximately(15 + (328 * 250 / 9.0), 0.001);
    }

    [Fact]
    public void AFarMiningTrip_CountsItsDrift_AndTheFuelBoughtWhereItLands()
    {
        // Slice 6.10c (D45): a drone at XB5C would drift to B7, beyond its tank (1 fuel, bought back at B7 for 79), then mine
        // gold at B14, 25 from B7, and sell it there. One of B14's eight ores comes up an extraction: at 3 units every 70
        // seconds, the 15-unit hold takes 40. Each CRUISE leg is one unit of fuel: 86, the system's average, at B14, which
        // sells none, and 79 at B7.
        var map = MiningFixture.Map();
        var drone = MiningFixture.Drone(waypoint: MiningFixture.XB5C);

        var option = RoleEstimator.Options(Context(map), drone, FleetRole.Mine, 20)
            .Single(candidate => candidate.JobKey == "mine|" + MiningPlanner.OpportunityKey(MiningFixture.B7, "GOLD_ORE"));

        var drift = RoleEstimator.DriftSeconds(map, MiningFixture.XB5C, MiningFixture.B7, RoleEstimator.DefaultEngineSpeed);
        var leg = RoleEstimator.FlightSeconds(map, MiningFixture.B7, [MiningFixture.B14], RoleEstimator.DefaultEngineSpeed);
        option.Seconds.Should().BeApproximately(
            drift + RoleEstimator.StopSeconds + leg + (40 * (70 + (RoleEstimator.TickSeconds / 2))) + leg + (2 * RoleEstimator.StopSeconds),
            0.001);
        option.Credits.Should().Be((15 * 114) - (79 + 86 + 79));
        option.Job.Should().Be($"GOLD_ORE at {MiningFixture.B14} for {MiningFixture.B7}, drifting there first");
    }

    [Fact]
    public void AFarSiphonTrip_CountsItsDrift()
    {
        // As a mining trip: a drone at C38 would drift to F48, beyond its tank, and siphon at D90, 13 from it.
        var map = SiphonFixture.MapWithAGasGiantNearF48();
        var drone = SiphonFixture.SiphonDrone(waypoint: SiphonFixture.C38, status: "IN_ORBIT");

        var option = RoleEstimator.Options(Context(map), drone, FleetRole.Siphon, 20)
            .Single(candidate => candidate.JobKey == "siphon|" + MiningPlanner.OpportunityKey(SiphonFixture.F48, "LIQUID_NITROGEN"));

        option.Seconds.Should().BeGreaterThan(RoleEstimator.DriftSeconds(map, SiphonFixture.C38, SiphonFixture.F48, RoleEstimator.DefaultEngineSpeed));
        option.Job.Should().EndWith("drifting there first");
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
        // H51 is LIMITED in copper and makes COPPER from it, at 200 against copper ore's 138.
        var markets = MiningFixture.Markets().Select(market => market.WaypointSymbol == MiningFixture.H51
            ? market with { TradeGoods = [.. market.TradeGoods, MiningFixture.Good("COPPER", "EXPORT", 200, 100, 60, "MODERATE")] }
            : market).ToArray();
        var map = new TradeMarketMap(MiningFixture.Waypoints, markets, new Dictionary<string, IReadOnlyList<string>> { ["COPPER"] = ["COPPER_ORE"] });
        var drone = MiningFixture.Drone(waypoint: MiningFixture.XB5C);
        var key = "mine|" + MiningPlanner.OpportunityKey(MiningFixture.H51, "COPPER_ORE");

        var without = RoleEstimator.Options(Context(map, share: 0), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);
        var with = RoleEstimator.Options(Context(map, share: 0.5), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);

        // Half of the 62 difference, three quarters of it at LIMITED, on each of the 15 units: 23.25 a unit, less than the 67
        // copper ore fetches at H51, so it counts in full (D49).
        (with.Credits - without.Credits).Should().Be((long)Math.Round(15 * 0.5 * 0.75 * (200 - 138)));
    }

    [Fact]
    public void AnOresChain_CountsAtMostWhatTheOreFetchesAtItsMarket()
    {
        // D49. H51 is LIMITED in copper ore and makes COPPER from it, at 400 against 138; H52 is SCARCE in copper and makes
        // ELECTRONICS from it, at 3,000 against 450. Half of three quarters of 262, and half of half of 2,550 on top:
        // 735.75 a unit, most of it the second step, while copper ore fetches 67 at H51. It counts 67, no more.
        var markets = MiningFixture.Markets().Select(market => market.WaypointSymbol switch
        {
            MiningFixture.H51 => market with { TradeGoods = [.. market.TradeGoods, MiningFixture.Good("COPPER", "EXPORT", 400, 200, 60, "MODERATE")] },
            MiningFixture.H52 => market with
            {
                TradeGoods = [.. market.TradeGoods, MiningFixture.Good("COPPER", "IMPORT", 450, 225, 60, "SCARCE"), MiningFixture.Good("ELECTRONICS", "EXPORT", 3_000, 1_500, 20, "MODERATE")],
            },
            _ => market,
        }).ToArray();
        var map = new TradeMarketMap(
            MiningFixture.Waypoints,
            markets,
            new Dictionary<string, IReadOnlyList<string>> { ["COPPER"] = ["COPPER_ORE"], ["ELECTRONICS"] = ["SILICON_CRYSTALS", "COPPER"] });
        var drone = MiningFixture.Drone(waypoint: MiningFixture.XB5C);
        var key = "mine|" + MiningPlanner.OpportunityKey(MiningFixture.H51, "COPPER_ORE");

        var without = RoleEstimator.Options(Context(map, share: 0), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);
        var with = RoleEstimator.Options(Context(map, share: 0.5), drone, FleetRole.Mine, 20).Single(option => option.JobKey == key);

        new ChainValues(map, 0.5).PerUnit(MiningFixture.H51, "COPPER_ORE").Should().BeApproximately(0.5 * ((0.75 * 262) + (0.5 * 2_550)), 0.001);
        (with.Credits - without.Credits).Should().Be(15 * 67);
    }

    [Fact]
    public void ATradeTrip_CountsTheChainAtMostItsMarginPerUnit()
    {
        // D49. EQUIPMENT from K85 to D41 earns 233 a unit (3,487 against 3,254), 20 units a trip. D41, MODERATE in it, makes
        // SHIP_PARTS from it at 7,721 against 7,032: half of 689 at a share of 100%, 344.5 a unit; 172.25 at 50%.
        var map = TradeFixture.Map();
        var ship = TradeFixture.CommandShip() with { EngineJson = """{"speed":36}""" };
        var route = TradeRoutePlanner.Rank(map, ship, 129_451, 200, new HashSet<string>())
            .Single(candidate => candidate.TradeSymbol == "EQUIPMENT" && candidate.SellWaypointSymbol == TradeFixture.D41);
        RoleOption Option(double share)
            => RoleEstimator.Options(Context(map, share), ship, FleetRole.Trade, 20).Single(candidate => candidate.JobKey == "trade|" + route.Key);

        route.Units.Should().Be(20);
        Option(0.5).Credits.Should().Be(route.Profit + (long)Math.Round(20 * 172.25), "the chain is less than the margin, and counts in full");
        Option(1).Credits.Should().Be(route.Profit + (20 * 233), "the chain counts no more than the margin");
    }

    [Fact]
    public void ASiphonTrip_ValuesItsOwnGasWhereItSellsIt_AndTheOthersItKeepsWhereTheyFetchMost()
    {
        // D49. A drone at C38 that siphons HYDROCARBON for C39 sells it there, at 60, not at G50's 90. The other gases it keeps
        // (D33) go on the trips after, where each fetches most: LIQUID_HYDROGEN at G50's 55, LIQUID_NITROGEN at E47's 50
        // (F48 pays 60, out of the drone's reach). A third of each: 55 a unit, less the fuel to C39, one unit bought at 80.
        var drone = SiphonFixture.SiphonDrone(waypoint: SiphonFixture.C38, status: "IN_ORBIT");

        var option = RoleEstimator.Options(Context(SiphonFixture.Map()), drone, FleetRole.Siphon, 20)
            .Single(candidate => candidate.JobKey == "siphon|" + MiningPlanner.OpportunityKey(SiphonFixture.C39, "HYDROCARBON"));

        option.Credits.Should().Be((15 * (60 + 55 + 50) / 3) - 80);
    }

    [Fact]
    public void ASiphonTrip_CountsEachGasesChainAtMostAtItsPrice()
    {
        // D49. G50 is SCARCE in LIQUID_HYDROGEN and makes PLASTICS from it, at 215 against 110; E47 is SCARCE in PLASTICS and
        // makes EQUIPMENT from it, at 3,400 against 230. Half of 105, and half of half of 3,170 on top: 845 a unit of liquid
        // hydrogen sold at G50, which pays 55 for it. It counts 55: 110 a unit.
        var markets = SiphonFixture.Markets().Select(market => market.WaypointSymbol switch
        {
            SiphonFixture.G50 => market with { TradeGoods = [.. market.TradeGoods, SiphonFixture.Good("PLASTICS", "EXPORT", 215, 105, 60, "MODERATE")] },
            SiphonFixture.E47 => market with
            {
                TradeGoods = [.. market.TradeGoods, SiphonFixture.Good("PLASTICS", "IMPORT", 230, 115, 60, "SCARCE"), SiphonFixture.Good("EQUIPMENT", "EXPORT", 3_400, 1_700, 20, "MODERATE")],
            },
            _ => market,
        }).ToArray();
        var map = new TradeMarketMap(
            SiphonFixture.Waypoints,
            markets,
            new Dictionary<string, IReadOnlyList<string>> { ["PLASTICS"] = ["LIQUID_HYDROGEN"], ["EQUIPMENT"] = ["ALUMINUM", "PLASTICS"] });
        var drone = SiphonFixture.SiphonDrone(waypoint: SiphonFixture.C38, status: "IN_ORBIT");
        var options = RoleEstimator.Options(Context(map, share: 0.5), drone, FleetRole.Siphon, 20);
        RoleOption Trip(string market, string gas)
            => options.Single(candidate => candidate.JobKey == "siphon|" + MiningPlanner.OpportunityKey(market, gas));

        new ChainValues(map, 0.5).PerUnit(SiphonFixture.G50, "LIQUID_HYDROGEN").Should().BeApproximately(0.5 * (105 + (0.5 * 3_170)), 0.001);

        // The trip for G50 sells its liquid hydrogen there (110) and keeps hydrocarbon for G50 (90) and liquid nitrogen for
        // E47 (50); less the fuel through C40 (75) to G50 (82).
        Trip(SiphonFixture.G50, "LIQUID_HYDROGEN").Credits.Should().Be((15 * (110 + 90 + 50) / 3) - (75 + 82));

        // The trip for C39 sells its hydrocarbon at C39 (60), which exchanges it and makes nothing from it, and keeps liquid
        // hydrogen for G50 (110) and liquid nitrogen for E47 (50); less the fuel to C39 (80).
        Trip(SiphonFixture.C39, "HYDROCARBON").Credits.Should().Be((15 * (60 + 110 + 50) / 3) - 80);
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

using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Tests.Probes;

/// <summary>
/// The probe plan's flights (PLAN.md slice 6.3, D29, D30) on X1-DC53 as it was on 2026-10-02: H51 and H52
/// orbit one planet, A1 and A2 another, 46 apart (143 s for a probe); XB5C sits 19 from H52; J58 is the far
/// corner, 761 from H52 (2,129 s, about 35 minutes).
/// </summary>
public sealed class ProbePlannerTests
{
    private const string H51 = "X1-DC53-H51";
    private const string H52 = "X1-DC53-H52";
    private const string A1 = "X1-DC53-A1";
    private const string A2 = "X1-DC53-A2";
    private const string XB5C = "X1-DC53-XB5C";
    private const string J58 = "X1-DC53-J58";

    private static readonly DateTimeOffset Now = new(2026, 10, 02, 16, 00, 00, TimeSpan.Zero);

    private static readonly IReadOnlyDictionary<string, WaypointPosition> Positions = new Dictionary<string, WaypointPosition>
    {
        [H51] = new(-18, 40),
        [H52] = new(-18, 40),
        [A1] = new(21, 16),
        [A2] = new(21, 16),
        [XB5C] = new(-15, 21),
        [J58] = new(435, -572),
    };

    [Fact]
    public void AFlight_TakesAsLongAsTheApiReckonsIt()
    {
        ProbePlanner.FlightSeconds(Positions[H52], Positions[H51], 9).Should().BeApproximately(15 + (25.0 / 9), 0.01);
        ProbePlanner.FlightSeconds(Positions[H52], Positions[A1], 9).Should().BeApproximately(15 + (46 * 25.0 / 9), 0.01);
        ProbePlanner.FlightSeconds(Positions[H52], Positions[A1], 0).Should().BeApproximately(15 + (46 * 25.0 / 9), 0.01);
    }

    [Fact]
    public void AProbe_GoesToTheMarketWhosePricesAreOldest_EachSecondOfFlightCountingTwice()
    {
        // H51 is next door but was seen 30 minutes ago; A1, 143 s away, an hour ago; J58, 35 minutes away,
        // 90 minutes ago. A1: 3,600 - 2 x 143; H51: 1,800 - 2 x 18; J58: 5,400 - 2 x 2,129.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52)],
            Market(H51, minutesAgo: 30),
            Market(A1, minutesAgo: 60),
            Market(J58, minutesAgo: 90)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", A1, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void AFarMarket_GoesFirst_OnceItIsStalerByTwiceItsFlight()
    {
        ProbePlanner.Plan(Snapshot([Probe("PROBE-1", H52)], Market(H51, minutesAgo: 10), Market(J58, minutesAgo: 90)))
            .Single().WaypointSymbol.Should().Be(J58);
        ProbePlanner.Plan(Snapshot([Probe("PROBE-1", H52)], Market(H51, minutesAgo: 10), Market(J58, minutesAgo: 60)))
            .Single().WaypointSymbol.Should().Be(H51);
    }

    [Fact]
    public void AMarketNeverSeen_IsTheOldest()
    {
        // B45 left J58 unseen for the whole reset.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52)],
            Market(H51, minutesAgo: 600),
            new ProbeMarket(J58, DateTimeOffset.MinValue)));

        moves.Single().WaypointSymbol.Should().Be(J58);
    }

    [Fact]
    public void MarketsSeenWithinTheInterval_AreNotDue_SoTheProbeStays()
    {
        ProbePlanner.Plan(Snapshot([Probe("PROBE-1", H52)], Market(H51, minutesAgo: 4), Market(A1, minutesAgo: 2)))
            .Should().BeEmpty();
    }

    [Fact]
    public void EachMarket_GoesToTheProbeNearestIt()
    {
        // Taking the probes in turn would send PROBE-1, at A2, to H51 when H51 is staler; scored as pairs,
        // each probe takes the market next door.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A2), Probe("PROBE-2", H52)],
            Market(H51, minutesAgo: 40),
            Market(A1, minutesAgo: 30)));

        moves.Should().BeEquivalentTo([
            new ProbeMove("PROBE-1", A1, ForPurchase: false, ShipType: string.Empty),
            new ProbeMove("PROBE-2", H51, ForPurchase: false, ShipType: string.Empty),
        ]);
    }

    [Fact]
    public void AMarketAProbeIsAtOrFliesTo_IsTaken()
    {
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52), Probe("PROBE-2", A1, free: false), Probe("PROBE-3", H51, free: false)],
            Market(A1, minutesAgo: 60),
            Market(H51, minutesAgo: 60),
            Market(XB5C, minutesAgo: 10)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", XB5C, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void WithAProbeAtEveryMarket_TheProbesStay()
    {
        // D29's long-term goal: every market has a probe of its own, and the market watch keeps it fresh.
        ProbePlanner.Plan(Snapshot(
                [Probe("PROBE-1", H51), Probe("PROBE-2", H52), Probe("PROBE-3", A1)],
                Market(H51, minutesAgo: 60),
                Market(H52, minutesAgo: 60),
                Market(A1, minutesAgo: 60)))
            .Should().BeEmpty();
    }

    [Fact]
    public void WithAProbeForEveryMarket_TheSpares_SettleAtTheMarketsWithoutOne_DueOrNot()
    {
        // B69: four probes for four markets, three of them at A1, where they were bought. Our other ships keep A2 and H52
        // fresh, so neither is ever due, and the spares stayed at the shipyard: seven at X1-FJ91-C46 for a day.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A1), Probe("PROBE-2", H51), Probe("PROBE-3", A1), Probe("PROBE-4", A1)],
            Market(A1, minutesAgo: 1),
            Market(A2, minutesAgo: 1),
            Market(H51, minutesAgo: 1),
            Market(H52, minutesAgo: 2)));

        moves.Should().BeEquivalentTo([
            new ProbeMove("PROBE-3", A2, ForPurchase: false, ShipType: string.Empty),
            new ProbeMove("PROBE-4", H52, ForPurchase: false, ShipType: string.Empty),
        ]);
    }

    [Fact]
    public void WithAProbeForEveryMarket_AProbeKeepsItsMarket_AndASpareTakesTheOneWithout()
    {
        // B69: PROBE-1, next door, left H51 for H52 as soon as H52 was due, and H51 was due five minutes later. In
        // X1-FJ91's clusters the probes hopped between neighbours some 75 times an hour, and the spares, further away,
        // never won a market.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H51), Probe("PROBE-2", A1), Probe("PROBE-3", A1)],
            Market(H51, minutesAgo: 1),
            Market(H52, minutesAgo: 10),
            Market(A1, minutesAgo: 1)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-3", H52, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void WithAProbeForEveryMarket_AProbeBackFromACall_TakesTheMarketWithoutOne()
    {
        // D30: once the purchase is made, the probe at the shipyard, which is no market, flies on.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52), Probe("PROBE-2", A1)],
            Market(H51, minutesAgo: 1),
            Market(A1, minutesAgo: 1)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", H51, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void WithFewerProbesThanMarkets_AProbeStillLeavesItsMarket_ForADueOne()
    {
        // D29: until there is a probe for every market, the probes drift between nearby markets.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H51), Probe("PROBE-2", A1)],
            Market(H51, minutesAgo: 1),
            Market(H52, minutesAgo: 10),
            Market(A1, minutesAgo: 1)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", H52, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void AShipyardThatCalls_GetsTheNearestFreeProbe_BeforeAnyMarket()
    {
        // D30: the mining plan can afford a drone at H52, where none of our ships is. XB5C, seen a minute ago, makes it fewer
        // probes than markets, so the other probe roams.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A1), Probe("PROBE-2", J58)],
            [Call(H52, "SHIP_MINING_DRONE")],
            Market(J58, minutesAgo: 1),
            Market(A2, minutesAgo: 600),
            Market(XB5C, minutesAgo: 1)));

        moves.Should().BeEquivalentTo([
            new ProbeMove("PROBE-1", H52, ForPurchase: true, ShipType: "SHIP_MINING_DRONE"),
            new ProbeMove("PROBE-2", A2, ForPurchase: false, ShipType: string.Empty),
        ]);
    }

    [Fact]
    public void AProbeAtAShipyardThatCalls_StaysForThePurchase()
    {
        ProbePlanner.Plan(Snapshot([Probe("PROBE-1", A2)], [Call(A2, "SHIP_PROBE")], Market(H51, minutesAgo: 600)))
            .Should().BeEmpty();
    }

    [Fact]
    public void AShipyardWhereOneOfOurShipsIs_NeedsNoProbe()
    {
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A1)],
            [Call(H52, "SHIP_MINING_DRONE")],
            [H52],
            Market(A2, minutesAgo: 30)));

        moves.Should().ContainSingle().Which.WaypointSymbol.Should().Be(A2);
    }

    [Fact]
    public void AProbeInFlight_IsLeftAlone()
    {
        ProbePlanner.Plan(Snapshot([Probe("PROBE-1", A1, free: false)], [Call(H52, "SHIP_PROBE")], Market(H51, minutesAgo: 600)))
            .Should().BeEmpty();
    }

    [Fact]
    public void ASystemWithMoreProbesThanMarkets_SparesTheOnesItWouldSettleNowhere()
    {
        // Slice 6.28 (B69): two markets, four probes. PROBE-1 keeps A1 and PROBE-2 H51; PROBE-3, a second probe at A1, and
        // PROBE-4, at XB5C (no market here), are spares. With PROBE-5 on its way to H51, which can't be lent in flight,
        // PROBE-2 is a spare too: PROBE-5 takes H51.
        var snapshot = Snapshot(
            [Probe("PROBE-1", A1), Probe("PROBE-2", H51), Probe("PROBE-3", A1), Probe("PROBE-4", XB5C)],
            Market(A1, minutesAgo: 1),
            Market(H51, minutesAgo: 1));

        ProbePlanner.Surplus(snapshot).Select(probe => probe.Symbol).Should().Equal("PROBE-3", "PROBE-4");
        ProbePlanner.Surplus(snapshot with { Probes = [.. snapshot.Probes, Probe("PROBE-5", H51, free: false)] })
            .Select(probe => probe.Symbol).Should().Equal("PROBE-2", "PROBE-3", "PROBE-4");
        ProbePlanner.Surplus(snapshot with { Probes = [.. snapshot.Probes.Take(3)] })
            .Select(probe => probe.Symbol).Should().Equal(["PROBE-3"], "as many as it has too many");
        ProbePlanner.Surplus(snapshot with { Probes = [.. snapshot.Probes.Take(2)] }).Should().BeEmpty();
    }

    [Fact]
    public void AProbeComingIn_TakesTheMarketARoamingProbeWouldTake_FromTheGate()
    {
        // Slice 6.28: from the gate at H52 (next door to H51), H51 seen half an hour ago beats A1 seen 31 minutes ago less
        // twice its 143 s; a market never seen goes first; one with a probe at it or on its way is taken.
        var snapshot = Snapshot([], Market(H51, minutesAgo: 30), Market(A1, minutesAgo: 31));

        ProbePlanner.Entry(snapshot, H52, 9).Should().Be(H51);
        ProbePlanner.Entry(snapshot with { Markets = [.. snapshot.Markets, new ProbeMarket(J58, DateTimeOffset.MinValue)] }, H52, 9).Should().Be(J58);
        ProbePlanner.Entry(snapshot with { Probes = [Probe("PROBE-1", H51, free: false)] }, H52, 9).Should().Be(A1);
        ProbePlanner.Entry(snapshot with { Probes = [Probe("PROBE-1", H51, free: false), Probe("PROBE-2", A1)] }, H52, 9).Should().BeEmpty();
        ProbePlanner.Entry(snapshot, "X1-DC53-UNKNOWN", 9).Should().Be(A1, "without the gate's position the oldest prices win");
    }

    [Fact]
    public void WithFewerProbesThanMarkets_AProbeAtAShipyard_StaysParked_AndTheOthersRoam()
    {
        // Slice 6.32, D110: "Stays parked", so a purchase at A2 needs no probe called (D30). Before, PROBE-1 left A2 for the
        // staler H51, as PROBE-2 leaves A1.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A2), Probe("PROBE-2", A1)],
            Shipyard(A2, minutesAgo: 1),
            Market(A1, minutesAgo: 1),
            Market(H51, minutesAgo: 60)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-2", H51, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void AShipyardWithoutAProbe_GetsTheNearestFreeProbe_BeforeADueMarket()
    {
        // D109, D110: shipyards first. A2 was seen a minute ago and isn't due; H51 is an hour old.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52)],
            Shipyard(A2, minutesAgo: 1),
            Market(H51, minutesAgo: 60),
            Market(XB5C, minutesAgo: 60)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", A2, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void AShipyardThatSellsExplorers_GetsAProbe_BeforeEveryOtherShipyard_HoweverFar()
    {
        // D109: "with shipyards with explorer ships being even higher priority than that". J58, the far corner, goes first,
        // to the probe nearest it (PROBE-2 at A1, 719 away; PROBE-1 at H52 is 761); then H51, next door to PROBE-1. XB5C,
        // an hour old, waits.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H52), Probe("PROBE-2", A1)],
            Shipyard(H51, minutesAgo: 60),
            Shipyard(J58, minutesAgo: 1, explorer: true),
            Market(A1, minutesAgo: 1),
            Market(XB5C, minutesAgo: 60)));

        moves.Should().BeEquivalentTo([
            new ProbeMove("PROBE-2", J58, ForPurchase: false, ShipType: string.Empty),
            new ProbeMove("PROBE-1", H51, ForPurchase: false, ShipType: string.Empty),
        ]);
    }

    [Fact]
    public void AProbeParkedAtAShipyard_GivesWayToAShipyardThatSellsExplorers()
    {
        // D109: a purchase at H51 drew the one probe there (D30), and it stayed parked once the purchase was made. J58, which
        // sells explorers, comes first: it moves on.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", H51)],
            Shipyard(H51, minutesAgo: 1),
            Shipyard(J58, minutesAgo: 1, explorer: true),
            Market(A1, minutesAgo: 60)));

        moves.Should().ContainSingle().Which.Should().Be(new ProbeMove("PROBE-1", J58, ForPurchase: false, ShipType: string.Empty));
    }

    [Fact]
    public void WithAProbeForEveryMarket_ASpareTakesAShipyardFirst()
    {
        // B69's settling, shipyards first (D109): H51's prices are an hour older than H52's, but H52 is a shipyard.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A1), Probe("PROBE-2", A1), Probe("PROBE-3", A1)],
            Market(A1, minutesAgo: 1),
            Shipyard(H52, minutesAgo: 1),
            Market(H51, minutesAgo: 60)));

        moves.Should().BeEquivalentTo([
            new ProbeMove("PROBE-2", H52, ForPurchase: false, ShipType: string.Empty),
            new ProbeMove("PROBE-3", H51, ForPurchase: false, ShipType: string.Empty),
        ]);
    }

    [Fact]
    public void AProbeComingIn_TakesAShipyardThatSellsExplorers_ThenAnotherShipyard_ThenTheMarketARoamingProbeWouldTake()
    {
        // D109: from the gate at H52, the shipyard J58 sells explorers; A2 is the nearest other shipyard, before XB5C's.
        var snapshot = Snapshot([], Market(H51, minutesAgo: 600), Shipyard(XB5C, minutesAgo: 1), Shipyard(A2, minutesAgo: 1), Shipyard(J58, minutesAgo: 1, explorer: true));

        ProbePlanner.Entry(snapshot, H52, 9).Should().Be(J58);
        ProbePlanner.Entry(snapshot with { Probes = [Probe("PROBE-1", J58, free: false)] }, H52, 9).Should().Be(XB5C, "it is nearer the gate than A2");
        ProbePlanner.Entry(snapshot with { Probes = [Probe("PROBE-1", J58, free: false), Probe("PROBE-2", XB5C), Probe("PROBE-3", A2)] }, H52, 9)
            .Should().Be(H51);
    }

    [Fact]
    public void AProbe_CountsWhereItsFlightGoes_ElseWhereItLands_ElseWhereItIs()
    {
        // B15, slice 6.28: a probe that only passes through X1-GT9 on its way to X1-BC61 counts for X1-BC61.
        var atGate = new ShipModel("PROBE-1", "X1-GT9", "X1-GT9-E10Z", "IN_ORBIT", "CRUISE", 0, 0);
        var flying = atGate with { Status = "IN_TRANSIT", DestWaypointSymbol = "X1-GT9-AE7B", ArrivesAt = Now.AddMinutes(5) };

        ProbePlanner.Whereabouts(atGate, new DeployProbeGoal { TargetWaypointSymbol = "X1-BC61-EE6B" }).Should().Be(("X1-BC61-EE6B", "X1-BC61"));
        ProbePlanner.Whereabouts(atGate, null).Should().Be(("X1-GT9-E10Z", "X1-GT9"));
        ProbePlanner.Whereabouts(atGate, new DeployProbeGoal { TargetWaypointSymbol = "X1-BC61-EE6B", Status = GoalStatus.Completed })
            .Should().Be(("X1-GT9-E10Z", "X1-GT9"), "a flight that ended holds nothing");
        ProbePlanner.Whereabouts(flying, null).Should().Be(("X1-GT9-AE7B", "X1-GT9"));
    }

    private static ProbeShip Probe(string symbol, string waypointSymbol, bool free = true) => new(symbol, waypointSymbol, free, 9);

    private static ProbeMarket Market(string waypointSymbol, int minutesAgo) => new(waypointSymbol, Now.AddMinutes(-minutesAgo));

    private static ProbeMarket Shipyard(string waypointSymbol, int minutesAgo, bool explorer = false)
        => Market(waypointSymbol, minutesAgo) with { Shipyard = explorer ? ShipyardKind.Explorer : ShipyardKind.Shipyard };

    private static ShipyardCall Call(string waypointSymbol, string shipType) => new(waypointSymbol, shipType, Now.AddSeconds(-10), Now.AddSeconds(-5));

    private static ProbeSnapshot Snapshot(IReadOnlyList<ProbeShip> probes, params ProbeMarket[] markets)
        => Snapshot(probes, [], [], markets);

    private static ProbeSnapshot Snapshot(IReadOnlyList<ProbeShip> probes, IReadOnlyList<ShipyardCall> calls, params ProbeMarket[] markets)
        => Snapshot(probes, calls, [], markets);

    private static ProbeSnapshot Snapshot(
        IReadOnlyList<ProbeShip> probes,
        IReadOnlyList<ShipyardCall> calls,
        IReadOnlyList<string> otherShipsAt,
        params ProbeMarket[] markets) => new()
    {
        Positions = Positions,
        Markets = markets,
        Probes = probes,
        ShipsAt = probes.Where(probe => probe.IsFree).Select(probe => probe.WaypointSymbol)
            .Concat(otherShipsAt)
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
        Calls = calls,
        Now = Now,
        DueAfter = TimeSpan.FromMinutes(5),
    };
}

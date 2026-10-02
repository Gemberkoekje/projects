using FluentAssertions;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;

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
    public void AShipyardThatCalls_GetsTheNearestFreeProbe_BeforeAnyMarket()
    {
        // D30: the mining plan can afford a drone at H52, where none of our ships is.
        var moves = ProbePlanner.Plan(Snapshot(
            [Probe("PROBE-1", A1), Probe("PROBE-2", J58)],
            [Call(H52, "SHIP_MINING_DRONE")],
            Market(J58, minutesAgo: 1),
            Market(A2, minutesAgo: 600)));

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

    private static ProbeShip Probe(string symbol, string waypointSymbol, bool free = true) => new(symbol, waypointSymbol, free, 9);

    private static ProbeMarket Market(string waypointSymbol, int minutesAgo) => new(waypointSymbol, Now.AddMinutes(-minutesAgo));

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

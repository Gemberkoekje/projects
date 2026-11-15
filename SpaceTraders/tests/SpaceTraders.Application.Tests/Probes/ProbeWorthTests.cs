using FluentAssertions;
using SpaceTraders.Application.Probes;
using SpaceTraders.Application.Services;

namespace SpaceTraders.Application.Tests.Probes;

/// <summary>
/// Slice 6.71 (D151), asked on 2026-11-15: "putting a probe up 10 minutes before the server resets does not give any
/// value at all". One more probe is worth the best promise of the markets no probe watches; a market never seen counts
/// as the system's best seen market, else the average of those elsewhere that promise anything ("System's best, else
/// average").
/// </summary>
public sealed class ProbeWorthTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 10, 00, 00, TimeSpan.Zero);

    [Fact]
    public void OneMoreProbe_IsWorthTheBestPromise_OfTheMarketsNoProbeWatches()
    {
        var worth = ProbeWorth.Of(
            [Seen("A1"), Seen("A2"), Seen("A3")],
            [Probe("A1")],
            Promises(("A1", 50_000), ("A2", 3_000), ("A3", 8_000)),
            []);

        worth.Should().Be(ShipWorth.Of(8_000, ProbeWorth.Basis), "A1's probe makes sure of its 50,000 already");
    }

    [Fact]
    public void AMarketNeverSeen_CountsAsTheSystemsBestSeenMarket_WatchedOrNot()
    {
        var worth = ProbeWorth.Of(
            [Seen("A1"), Seen("A2"), Market("A3", DateTimeOffset.MinValue)],
            [Probe("A1")],
            Promises(("A1", 50_000), ("A2", 3_000)),
            [900_000]);

        worth.PerHour.Should().Be(50_000);
    }

    [Fact]
    public void InASystemWithNoMarketSeen_ANeverSeenMarket_CountsAsTheAverageElsewhere()
    {
        var worth = ProbeWorth.Of(
            [Market("B1", DateTimeOffset.MinValue), Market("B2", DateTimeOffset.MinValue)],
            [],
            Promises(),
            [2_000, 4_000]);

        worth.Should().Be(ShipWorth.Of(3_000, ProbeWorth.Basis));
    }

    [Fact]
    public void WithNoPromiseKnown_TheWorthIsUnknown()
    {
        ProbeWorth.Of([Market("B1", DateTimeOffset.MinValue)], [], Promises(), []).Known.Should().BeFalse();
    }

    [Fact]
    public void MarketsSeenLately_PromiseNothing_SoTheWorthIsZero()
    {
        // D134: a market seen lately promises nothing; a probe waits till one of them is due again.
        var worth = ProbeWorth.Of([Seen("A1"), Seen("A2")], [], Promises(), [5_000]);

        worth.Should().Be(ShipWorth.Of(0, ProbeWorth.Basis));
    }

    private static ProbeMarket Seen(string symbol) => Market(symbol, Now.AddMinutes(-40));

    private static ProbeMarket Market(string symbol, DateTimeOffset lastSeenAt) => new(symbol, lastSeenAt);

    private static ProbeShip Probe(string at) => new($"PROBE-{at}", at, IsFree: false, Speed: 10);

    private static Dictionary<string, double> Promises(params (string Market, double Promise)[] promises)
        => promises.ToDictionary(entry => entry.Market, entry => entry.Promise, StringComparer.OrdinalIgnoreCase);
}

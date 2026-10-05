using FluentAssertions;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>D88: how long the routes a new cargo ship would take have waited for one, without a break.</summary>
public sealed class TradeShipDemandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARoute_WaitsFromWhenItWasFirstSeen_AndIsForgottenOnceItNoLongerWaits()
    {
        var demand = new TradeShipDemand();

        demand.Note(["A|B|FOOD"], Now).Should().Be(TimeSpan.Zero);
        demand.Note(["A|B|FOOD", "C|D|CLOTHING"], Now.AddMinutes(20)).Should().Be(TimeSpan.FromMinutes(20));
        demand.Note(["C|D|CLOTHING"], Now.AddMinutes(25)).Should().Be(TimeSpan.FromMinutes(5), "FOOD went in the meantime");
        demand.Note(["A|B|FOOD"], Now.AddMinutes(30)).Should().Be(TimeSpan.Zero, "a route that comes back waits anew");
        demand.Note([], Now.AddMinutes(31)).Should().Be(TimeSpan.Zero);
    }
}

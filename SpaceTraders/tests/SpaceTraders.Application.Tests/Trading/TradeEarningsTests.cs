using FluentAssertions;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// D87, asked on 2026-10-05 ("Cap at realized"): what the trade trips that ended in the last two hours made per hour of their
/// time caps the role board's trade estimates.
/// </summary>
public sealed class TradeEarningsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutATradeTripInTheLastTwoHours_NothingCapsTheEstimates()
    {
        var earnings = new TradeEarnings();
        earnings.Ended(Now.AddHours(-3), Now.AddHours(-2.5), 100_000);

        earnings.PerHour(Now).Should().Be(double.PositiveInfinity);
        new TradeEarnings().PerHour(Now).Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void TheRate_IsWhatTheRecentTripsMade_OverTheirHours()
    {
        // 30,000 in 30 minutes and 10,000 in 90: 40,000 in two hours. The trip that ended three hours ago counts no more.
        var earnings = new TradeEarnings();
        earnings.Ended(Now.AddMinutes(-100), Now.AddMinutes(-70), 30_000);
        earnings.Ended(Now.AddMinutes(-100), Now.AddMinutes(-10), 10_000);
        earnings.Ended(Now.AddHours(-5), Now.AddHours(-3), 1_000_000);

        earnings.PerHour(Now).Should().BeApproximately(20_000, 0.001);
    }

    [Fact]
    public void ATripOfNoTime_CountsAMinute()
    {
        // SPECTER-1 sold the 40 HYDROCARBON it held where it lay: 2,000 in no time, not an infinite rate.
        var earnings = new TradeEarnings();
        earnings.Ended(Now, Now, 2_000);

        earnings.PerHour(Now).Should().BeApproximately(120_000, 0.001);
    }
}

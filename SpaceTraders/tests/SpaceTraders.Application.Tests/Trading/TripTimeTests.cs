using FluentAssertions;
using SpaceTraders.Application.Trading;

namespace SpaceTraders.Application.Tests.Trading;

/// <summary>
/// Slice 6.72 (D152): a trade trip must end <c>Trade.ResetSlackMinutes</c> before the server's next reset, as the reset takes
/// the ship and its cargo.
/// </summary>
public sealed class TripTimeTests
{
    private static readonly DateTimeOffset Reset = new(2026, 11, 22, 13, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(60, 5, 3_300)]
    [InlineData(60, 0, 3_600)]
    [InlineData(60, -5, 3_600)]
    [InlineData(3, 5, -120)]
    public void TheSecondsLeft_RunUntilTheSlackBeforeTheReset(int minutesBefore, int slackMinutes, double seconds)
        => TripTime.SecondsLeftBefore(Reset, Reset.AddMinutes(-minutesBefore), slackMinutes).Should().Be(seconds);

    [Fact]
    public void WithNoResetKnown_AnyTripMayStart()
        => TripTime.SecondsLeftBefore(DateTimeOffset.MaxValue, Reset, 5).Should().Be(double.PositiveInfinity);
}

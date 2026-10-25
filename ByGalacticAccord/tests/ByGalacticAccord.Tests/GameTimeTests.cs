using System;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Game time (design §8.3): one tick is one game hour, shown as dates and durations, never ticks.</summary>
public sealed class GameTimeTests
{
    // The typographic minus, U+2212, which negative days use.
    private const string Minus = "\u2212";

    [Theory]
    [InlineData(0L, "Day 0 · 00:00")]
    [InlineData(9L, "Day 0 · 09:00")]
    [InlineData(24L, "Day 1 · 00:00")]
    [InlineData(110L, "Day 4 · 14:00")]
    [InlineData(1_000_000L, "Day 41666 · 16:00")]
    [InlineData(-1L, "Day " + Minus + "1 · 23:00")]
    [InlineData(-24L, "Day " + Minus + "1 · 00:00")]
    [InlineData(-25L, "Day " + Minus + "2 · 23:00")]
    public void Dates_read_as_day_and_hour(long tick, string expected)
    {
        Assert.Equal(expected, GameTime.FormatDate(tick));
    }

    [Theory]
    [InlineData(0L, "0h")]
    [InlineData(9L, "9h")]
    [InlineData(24L, "1d")]
    [InlineData(30L, "1d 6h")]
    [InlineData(56L, "2d 8h")]
    [InlineData(230L, "9d 14h")]
    public void Durations_read_in_days_and_hours(long ticks, string expected)
    {
        Assert.Equal(expected, GameTime.FormatDuration(ticks));
    }

    [Fact]
    public void A_duration_is_never_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameTime.FormatDuration(-1));
    }

    [Theory]
    [InlineData(140L, 110L, "in 1d 6h")]
    [InlineData(110L, 140L, "1d 6h ago")]
    [InlineData(110L, 110L, "now")]
    public void Relative_times_say_which_way(long tick, long now, string expected)
    {
        Assert.Equal(expected, GameTime.FormatRelative(tick, now));
    }

    [Fact]
    public void Day_and_hour_round_trip_through_ticks()
    {
        for (long tick = -100; tick <= 100; tick++)
        {
            Assert.Equal(tick, GameTime.At(GameTime.Day(tick), GameTime.HourOfDay(tick)));
            Assert.InRange(GameTime.HourOfDay(tick), 0, 23);
        }

        Assert.Equal(GameTime.Days(2), GameTime.At(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameTime.At(1, 24));
    }
}

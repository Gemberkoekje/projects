using Curator.Core.Rules;

namespace Curator.Core.Tests;

public sealed class CalendarTests
{
    [Theory]
    [InlineData(1, "3 Seedmonth")]
    [InlineData(28, "30 Seedmonth")]
    [InlineData(29, "1 Bloomtide")]
    [InlineData(0, "2 Seedmonth")]
    [InlineData(-2, "30 Frostwane")]
    [InlineData(-32, "30 Emberfall")]
    public void DaysPrintAsCalendarDates(int day, string expected) =>
        Assert.Equal(expected, CalendarRules.DateText(TestWorld.Content.Schedule.Calendar, day));
}

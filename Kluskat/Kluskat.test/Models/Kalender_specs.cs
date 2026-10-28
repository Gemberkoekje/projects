namespace Kluskat.test.Models;

public sealed class Kalender_specs
{
    [Test]
    public void the_week_starts_on_monday_at_local_midnight()
    {
        // Sunday 1 November 2026, 23:30 in Amsterdam = 22:30 UTC: still the week of Monday 26 October.
        var zondagavond = new DateTime(2026, 11, 01, 22, 30, 00, DateTimeKind.Utc);

        Kalender.BeginVanDeWeek(zondagavond).Should().Be(new DateTime(2026, 10, 25, 23, 00, 00, DateTimeKind.Utc));
    }

    [Test]
    public void a_late_evening_in_utc_can_already_be_tomorrow_locally()
    {
        var utc = new DateTime(2026, 07, 01, 22, 30, 00, DateTimeKind.Utc);

        Kalender.Datum(utc).Should().Be(new DateOnly(2026, 07, 02), because: "Amsterdam is UTC+2 in summer");
    }
}

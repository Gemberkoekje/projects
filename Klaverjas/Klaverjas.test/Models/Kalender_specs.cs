using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Models;

public sealed class Kalender_specs
{
    [TestCase(2026, 01, 12, 3)]
    [TestCase(2026, 11, 16, 47)]
    [TestCase(2026, 12, 28, 53)]
    public void week_numbers_are_iso_weeks_as_on_the_sheet(int jaar, int maand, int dag, int week)
    {
        Kalender.Week(new DateOnly(jaar, maand, dag)).Should().Be(week);
    }

    [TestCase(01, 1)]
    [TestCase(03, 1)]
    [TestCase(04, 2)]
    [TestCase(11, 4)]
    public void months_fall_into_quarters(int maand, int kwartaal)
    {
        Kalender.Kwartaal(new DateOnly(2026, maand, 01)).Should().Be(kwartaal);
    }

    [Test]
    public void the_month_columns_are_every_play_day_of_the_month()
    {
        Kalender.Speeldagen(2026, 11, DayOfWeek.Monday).Select(d => d.Day).Should().Equal(2, 9, 16, 23, 30);
    }

    [Test]
    public void before_any_evening_the_next_is_the_first_evening_of_the_season()
    {
        Kalender.VolgendeAvond(Standaard, []).Should().Be(new DateOnly(2026, 01, 12));
    }

    [Test]
    public void the_next_evening_is_the_play_day_after_the_latest_entered_one()
    {
        Kalender.VolgendeAvond(Standaard, [Wk45, Wk47, Wk46]).Should().Be(new DateOnly(2026, 11, 23));
    }

    [Test]
    public void the_next_evening_skips_evenings_without_play()
    {
        var instellingen = Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = new DateOnly(2026, 11, 23), Reden = "Feest" }] };

        Kalender.VolgendeAvond(instellingen, [Wk47]).Should().Be(new DateOnly(2026, 11, 30));
    }
}

using DatumPrikker.WebApp;

namespace DatumPrikker.test.Web;

public sealed class Weergave_specs
{
    [Test]
    public void a_span_of_dates_reads_compactly()
    {
        Weergave.Bereik(Fixtures.Poll(Fixtures.Evening(12))).Should().Be("12 dec");
        Weergave.Bereik(Fixtures.Poll(Fixtures.Evening(12), Fixtures.WholeDay(14))).Should().Be("12 – 14 dec");

        var acrossMonths = Fixtures.Poll(new DateOption { Date = new DateOnly(2026, 11, 30) }, Fixtures.WholeDay(02));
        Weergave.Bereik(acrossMonths).Should().Be("30 nov – 2 dec");
    }

    [Test]
    public void an_answer_summarises_its_choices()
    {
        var poll = Fixtures.Poll();

        Weergave.Samenvatting(Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Yes, Availability.Yes, Availability.No))
            .Should().Be("2× ja, 1× nee");
    }

    [Test]
    public void a_moment_reads_in_dutch_local_time()
    {
        Weergave.Moment(new DateTime(2026, 11, 09, 19, 15, 00, DateTimeKind.Utc)).Should().Be("9 november om 20:15");
    }

    [Test]
    public void a_calendar_tile_reads_month_day_and_weekday()
    {
        var date = new DateOnly(2026, 12, 05);

        Weergave.Maand(date).Should().Be("dec");
        Weergave.DagNummer(date).Should().Be("5");
        Weergave.Weekdag(date).Should().Be("zaterdag");
        Weergave.WeekdagKort(date).Should().Be("za");
    }

    [TestCase(2, 3, "66.7%")]
    [TestCase(3, 3, "100%")]
    [TestCase(0, 3, "0%")]
    [TestCase(1, 0, "0%")]
    public void a_share_becomes_a_css_percentage(int part, int total, string expected)
    {
        Weergave.Procent(part, total).Should().Be(expected);
    }
}

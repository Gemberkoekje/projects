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

    [Test]
    public void a_dates_counts_leave_out_maybe_when_nobody_said_it()
    {
        Weergave.Telling(new OptionTally { Yes = 5, Maybe = 1, No = 2 }).Should().Be("5× ja, 1× misschien");
        Weergave.Telling(new OptionTally { Yes = 3, No = 1 }).Should().Be("3× ja");
    }

    [Test]
    public void the_likely_date_says_how_it_was_chosen_and_who_decides()
    {
        var one = new LikelyDate { Leading = [new OptionTally { Yes = 5, Maybe = 1 }], AnswerCount = 7 };
        var tie = new LikelyDate { Leading = [new OptionTally { Yes = 4, Maybe = 1 }, new OptionTally { Yes = 4, Maybe = 1 }], AnswerCount = 6 };
        var alone = new LikelyDate { Leading = [new OptionTally { Yes = 1 }], AnswerCount = 1 };

        Weergave.Vermoedelijk(one, "Arie").Should().Be("Meeste ja, dan meeste misschien, uit 7 antwoorden. Nog niet definitief: dat beslist Arie.");
        Weergave.Vermoedelijk(tie, "Arie").Should().Be("Deze 2 staan gelijk: evenveel ja en misschien, uit 6 antwoorden. Nog niet definitief: dat beslist Arie.");
        Weergave.Vermoedelijk(alone, "Arie").Should().Be("Meeste ja, dan meeste misschien, uit 1 antwoord. Nog niet definitief: dat beslist Arie.");
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

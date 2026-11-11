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

    [Test]
    public void a_deadline_with_a_time_reads_as_a_moment()
    {
        var poll = WithDeadline("2026-12-04", "18:00");

        Weergave.AntwoordenTot(poll).Should().Be("Antwoorden kan tot vrijdag 4 december om 18:00");
        Weergave.SluitVanzelf(poll, passed: false).Should().Be("Sluit vanzelf op vrijdag 4 december om 18:00.");
        Weergave.SluitVanzelf(poll, passed: true).Should().Be("Vanzelf gesloten op vrijdag 4 december om 18:00.");
        Weergave.SluitKort(poll).Should().Be("sluit 4 december om 18:00");
    }

    [Test]
    public void a_deadline_without_a_time_reads_as_the_whole_day()
    {
        var poll = WithDeadline("2026-12-04", string.Empty);

        Weergave.AntwoordenTot(poll).Should().Be("Antwoorden kan tot en met vrijdag 4 december");
        Weergave.SluitVanzelf(poll, passed: false).Should().Be("Sluit vanzelf na vrijdag 4 december.");
        Weergave.SluitVanzelf(poll, passed: true).Should().Be("Vanzelf gesloten na vrijdag 4 december.");
        Weergave.SluitKort(poll).Should().Be("sluit na 4 december");
    }

    [Test]
    public void a_summer_deadline_reads_in_summer_time()
    {
        Weergave.AntwoordenTot(WithDeadline("2027-06-04", "18:00")).Should().Be("Antwoorden kan tot vrijdag 4 juni om 18:00");
        Weergave.AntwoordenTot(WithDeadline("2027-06-04", string.Empty)).Should().Be("Antwoorden kan tot en met vrijdag 4 juni");
    }

    [Test]
    public void a_poll_is_open_closed_or_picked()
    {
        var poll = Fixtures.Poll();
        var picked = poll.Pick(poll.Options[0].Id, Fixtures.Owner, Fixtures.Now).Value;

        (Weergave.Staat(poll, isOpen: true), Weergave.StaatKlasse(poll, isOpen: true)).Should().Be(("open", "label-open"));
        (Weergave.Staat(poll, isOpen: false), Weergave.StaatKlasse(poll, isOpen: false)).Should().Be(("gesloten", "label-dicht"));
        (Weergave.Staat(picked, isOpen: false), Weergave.StaatKlasse(picked, isOpen: false)).Should().Be(("gekozen", "label-gekozen"));
    }

    [Test]
    public void the_picked_date_reads_with_its_weekday()
    {
        var date = new DateOnly(2026, 12, 12);

        Weergave.DagMaand(date).Should().Be("zaterdag 12 december");
        Weergave.DagMaandKort(date).Should().Be("zaterdag 12 dec");
    }

    [Test]
    public void the_pick_asks_first_and_says_whether_it_closes_the_poll()
    {
        Weergave.KiesVraag(Fixtures.Evening(12), closes: true)
            .Should().Be("Dit wordt het: zaterdag 12 december, 19:00–22:00? Dan sluit de prikker en ziet iedereen met de link deze datum.");
        Weergave.KiesVraag(Fixtures.WholeDay(13), closes: false)
            .Should().Be("Dit wordt het: zondag 13 december, hele dag? Dan ziet iedereen met de link deze datum.");
    }

    [TestCase("Etentje met de buren", "etentje-met-de-buren.ics")]
    [TestCase("  Klusdag: schuur & tuin!  ", "klusdag-schuur-tuin.ics")]
    [TestCase("\U0001F389\U0001F389", "afspraak.ics")]
    public void the_agenda_file_is_named_after_the_title(string title, string expected)
    {
        Weergave.AgendaBestand(title).Should().Be(expected);
    }

    private static Poll WithDeadline(string day, string time)
    {
        var draft = Fixtures.Draft() with
        {
            DeadlineDate = DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture),
            DeadlineHasTime = time.Length > 0,
            DeadlineTime = time.Length > 0 ? TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture) : default,
        };

        return Poll.Create(draft, Fixtures.Owner, "k7Gm2xQa", Fixtures.Now).Value;
    }
}

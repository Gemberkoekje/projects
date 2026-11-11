using DatumPrikker.WebApp.Pages;

namespace DatumPrikker.test.Web;

/// <summary>D22: "Wanneer sluit hij? (optioneel)" on Nieuwe prikker, a day plus an optional time.</summary>
public sealed class DeadlineInput_specs
{
    [Test]
    public void left_empty_there_is_no_deadline()
    {
        new PollInput().TryParseDeadline(out var date, out var hasTime, out _, out _).Should().BeTrue();

        date.Should().Be(default);
        hasTime.Should().BeFalse();
    }

    [Test]
    public void a_day_alone_closes_at_the_end_of_that_day()
    {
        new PollInput { DeadlineDate = "2026-12-04" }.TryParseDeadline(out var date, out var hasTime, out _, out _).Should().BeTrue();

        date.Should().Be(new DateOnly(2026, 12, 04));
        hasTime.Should().BeFalse();
    }

    [Test]
    public void a_day_and_a_time()
    {
        new PollInput { DeadlineDate = "2026-12-04", DeadlineTime = "18:00" }.TryParseDeadline(out var date, out var hasTime, out var time, out _).Should().BeTrue();

        date.Should().Be(new DateOnly(2026, 12, 04));
        hasTime.Should().BeTrue();
        time.Should().Be(new TimeOnly(18, 0));
    }

    [TestCase("", "18:00", "kies ook een dag, of laat de tijd leeg.")]
    [TestCase("4-12-2026", "", "kies een dag.")]
    [TestCase("2026-12-04", "25:00", "die tijd klopt niet.")]
    public void a_half_or_wrongly_filled_deadline_says_what_is_wrong(string day, string time, string expected)
    {
        new PollInput { DeadlineDate = day, DeadlineTime = time }.TryParseDeadline(out _, out _, out _, out var error).Should().BeFalse();

        error.Should().Be(expected);
    }
}

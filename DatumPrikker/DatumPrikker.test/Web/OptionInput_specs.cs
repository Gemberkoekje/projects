using DatumPrikker.WebApp.Pages;

namespace DatumPrikker.test.Web;

public sealed class OptionInput_specs
{
    [Test]
    public void a_day_without_times_is_the_whole_day()
    {
        new OptionInput { Date = "2026-12-12" }.TryParse(out var option, out _).Should().BeTrue();

        option.Should().BeEquivalentTo(new DateOption { Date = new DateOnly(2026, 12, 12) });
    }

    [Test]
    public void a_day_with_both_times_is_a_time_range()
    {
        new OptionInput { Date = "2026-12-12", Start = "19:00", End = "22:30" }.TryParse(out var option, out _).Should().BeTrue();

        option.HasTime.Should().BeTrue();
        option.Start.Should().Be(new TimeOnly(19, 0));
        option.End.Should().Be(new TimeOnly(22, 30));
    }

    [TestCase("", "19:00", "22:00", "kies een dag.")]
    [TestCase("12-12-2026", "", "", "kies een dag.")]
    [TestCase("2026-12-12", "19:00", "", "vul een begin- én eindtijd in, of laat beide leeg voor de hele dag.")]
    [TestCase("2026-12-12", "25:00", "26:00", "die tijd klopt niet.")]
    public void a_half_filled_row_says_what_is_missing(string date, string start, string end, string expected)
    {
        new OptionInput { Date = date, Start = start, End = end }.TryParse(out _, out var error).Should().BeFalse();

        error.Should().Be(expected);
    }

    [Test]
    public void an_empty_row_is_blank()
    {
        new OptionInput().IsBlank.Should().BeTrue();
        new OptionInput { Start = "19:00" }.IsBlank.Should().BeFalse();
    }
}

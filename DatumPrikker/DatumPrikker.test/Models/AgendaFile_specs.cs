using System.Text;

namespace DatumPrikker.test.Models;

/// <summary>D22: "In je agenda zetten" downloads the picked date as an iCalendar file.</summary>
public sealed class AgendaFile_specs
{
    private const string Link = "https://datumprikker.gemberkoekje.nl/p/k7Gm2xQa";

    [Test]
    public void a_whole_day_is_a_date_event_ending_the_next_day()
    {
        var lines = Lines(Picked(Fixtures.WholeDay(31)));

        lines.Should().Contain("DTSTART;VALUE=DATE:20261231").And.Contain("DTEND;VALUE=DATE:20270101");
    }

    [Test]
    public void a_time_range_is_converted_from_amsterdam_time_to_utc()
    {
        Lines(Picked(Fixtures.Evening(12))).Should()
            .Contain("DTSTART:20261212T180000Z", "19:00 in winter is UTC+1")
            .And.Contain("DTEND:20261212T210000Z");

        var summer = new DateOption { Date = new DateOnly(2027, 06, 12), HasTime = true, Start = new TimeOnly(19, 0), End = new TimeOnly(22, 0) };
        Lines(Picked(summer)).Should()
            .Contain("DTSTART:20270612T170000Z", "19:00 in summer is UTC+2")
            .And.Contain("DTEND:20270612T200000Z");
    }

    [Test]
    public void a_time_range_across_the_switch_to_summer_time_gets_both_offsets()
    {
        // On 28 March 2027 the clocks jump from 02:00 to 03:00: 01:00 is still UTC+1, 04:00 already UTC+2.
        var night = new DateOption { Date = new DateOnly(2027, 03, 28), HasTime = true, Start = new TimeOnly(1, 0), End = new TimeOnly(4, 0) };

        Lines(Picked(night)).Should().Contain("DTSTART:20270328T000000Z").And.Contain("DTEND:20270328T020000Z");
    }

    [Test]
    public void the_title_place_and_description_are_escaped()
    {
        var poll = Picked(Fixtures.Evening(12)) with
        {
            Title = "Eten, drinken; dansen",
            Location = @"Bij Arie, nr. 14 \ achterom",
            Description = "Ik kook.\r\nJullie nemen\neen toetje mee, graag.",
        };

        var lines = Lines(poll);

        lines.Should().Contain(@"SUMMARY:Eten\, drinken\; dansen");
        lines.Should().Contain(@"LOCATION:Bij Arie\, nr. 14 \\ achterom");
        lines.Should().Contain(@"DESCRIPTION:Ik kook.\nJullie nemen\neen toetje mee\, graag.\n\n" + Link);
    }

    [Test]
    public void without_a_description_the_description_is_the_link_and_without_a_place_there_is_no_location()
    {
        var lines = Lines(Picked(Fixtures.Evening(12)) with { Description = string.Empty, Location = string.Empty });

        lines.Should().Contain("DESCRIPTION:" + Link);
        lines.Should().NotContain(line => line.StartsWith("LOCATION", StringComparison.Ordinal));
    }

    [Test]
    public void long_lines_are_folded_at_75_octets_without_splitting_characters()
    {
        var poll = Picked(Fixtures.Evening(12)) with { Description = string.Concat(Enumerable.Repeat("\u00c9\u00e9n toetje \U0001F370 per persoon. ", 12)) };

        var text = AgendaFile.For(poll, Link);

        text.Split("\r\n").Should().OnlyContain(line => Encoding.UTF8.GetByteCount(line) <= 75);
        text.Should().NotContain("�");
        Lines(poll).Should().Contain(@"DESCRIPTION:" + AgendaFile.Text(poll.Description) + @"\n\n" + Link);
    }

    [Test]
    public void every_line_ends_with_crlf_and_there_is_one_event()
    {
        var text = AgendaFile.For(Picked(Fixtures.Evening(12)), Link);

        text.Should().StartWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n").And.EndWith("END:VEVENT\r\nEND:VCALENDAR\r\n");
        text.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\n");
        Lines(Picked(Fixtures.Evening(12))).Count(line => line == "BEGIN:VEVENT").Should().Be(1);
    }

    [Test]
    public void a_changed_pick_keeps_the_uid_and_raises_the_sequence()
    {
        var poll = Fixtures.Poll(Fixtures.Evening(12), Fixtures.Evening(13));
        var first = poll.Pick(poll.Options[0].Id, Fixtures.Owner, Fixtures.Now.AddMinutes(5)).Value;
        var changed = first.Pick(poll.Options[1].Id, Fixtures.Owner, Fixtures.Now.AddDays(1)).Value;

        var before = Lines(first);
        var after = Lines(changed);

        before.Should().Contain($"UID:{poll.Id:D}");
        after.Should().Contain($"UID:{poll.Id:D}");
        Sequence(after).Should().BeGreaterThan(Sequence(before));
        after.Should().Contain("DTSTAMP:20261110T120000Z");
    }

    [Test]
    public void without_a_pick_there_is_no_agenda_file()
    {
        var unpicked = Fixtures.Poll();

        unpicked.Invoking(poll => AgendaFile.For(poll, Link)).Should().Throw<ArgumentException>();
    }

    private static Poll Picked(DateOption option)
    {
        var poll = Fixtures.Poll(option);
        return poll.Pick(poll.Options[0].Id, Fixtures.Owner, Fixtures.Now.AddDays(1)).Value;
    }

    /// <summary>The file's lines, unfolded (RFC 5545 3.1).</summary>
    private static string[] Lines(Poll poll)
        => AgendaFile.For(poll, Link).Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static int Sequence(IEnumerable<string> lines)
        => int.Parse(lines.Single(line => line.StartsWith("SEQUENCE:", StringComparison.Ordinal))["SEQUENCE:".Length..], System.Globalization.CultureInfo.InvariantCulture);
}

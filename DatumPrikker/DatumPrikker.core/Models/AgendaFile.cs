using System.Globalization;
using System.Text;

namespace DatumPrikker.core.Models;

/// <summary>
/// The picked date as an iCalendar file (RFC 5545), for "In je agenda zetten" (D22). Its UID is the
/// poll's id, so importing a changed pick replaces the earlier event instead of adding a second one.
/// </summary>
public static class AgendaFile
{
    /// <summary>The MIME type of the file.</summary>
    public const string ContentType = "text/calendar; charset=utf-8";

    // RFC 5545 3.1: lines longer than 75 octets are folded.
    private const int MaxLineOctets = 75;

    /// <summary>The event for a poll's picked date.</summary>
    /// <param name="poll">A poll with a pick.</param>
    /// <param name="link">The poll's link, added to the description.</param>
    /// <returns>The file's text, with CRLF line endings.</returns>
    public static string For(Poll poll, string link)
    {
        ArgumentNullException.ThrowIfNull(poll);

        if (!poll.HasPick)
        {
            throw new ArgumentException("Only a picked date goes in an agenda.", nameof(poll));
        }

        var option = poll.PickedOption();
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//Gemberkoekje//DatumPrikker//NL",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            "BEGIN:VEVENT",
            $"UID:{poll.Id:D}",
            $"SEQUENCE:{Sequence(poll).ToString(CultureInfo.InvariantCulture)}",
            $"DTSTAMP:{Utc(poll.PickedOn)}",
        };

        if (option.HasTime)
        {
            lines.Add($"DTSTART:{Utc(DutchTime.ToUtc(option.Date, option.Start))}");
            lines.Add($"DTEND:{Utc(DutchTime.ToUtc(option.Date, option.End))}");
        }
        else
        {
            // A whole day ends where the next one starts (RFC 5545 3.6.1).
            lines.Add($"DTSTART;VALUE=DATE:{Day(option.Date)}");
            lines.Add($"DTEND;VALUE=DATE:{Day(option.Date.AddDays(1))}");
        }

        lines.Add($"SUMMARY:{Text(poll.Title)}");
        if (poll.Location.Length > 0)
        {
            lines.Add($"LOCATION:{Text(poll.Location)}");
        }

        lines.Add($"DESCRIPTION:{Text(poll.Description.Length > 0 ? $"{poll.Description}\n\n{link}" : link)}");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var file = new StringBuilder();
        foreach (var line in lines)
        {
            file.Append(Fold(line)).Append("\r\n");
        }

        return file.ToString();
    }

    /// <summary>Escapes a TEXT value (RFC 5545 3.3.11): backslashes, semicolons, commas and line breaks.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Grows with every new pick, for calendars that only take an update with a higher SEQUENCE: the
    /// seconds between making the poll and the latest pick.
    /// </summary>
    private static int Sequence(Poll poll)
        => (int)Math.Clamp((poll.PickedOn - poll.CreatedOn).TotalSeconds, 0, int.MaxValue);

    private static string Utc(DateTime utc) => utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Folds a line into pieces of at most 75 octets; each next piece starts with a space.</summary>
    private static string Fold(string line)
    {
        var folded = new StringBuilder();
        var octets = 0;
        var index = 0;
        while (index < line.Length)
        {
            // Never split a surrogate pair: it is one character, up to 4 octets in UTF-8.
            var width = char.IsHighSurrogate(line[index]) && index + 1 < line.Length ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(line.AsSpan(index, width));
            if (octets + size > MaxLineOctets)
            {
                folded.Append("\r\n ");
                octets = 1;
            }

            folded.Append(line, index, width);
            octets += size;
            index += width;
        }

        return folded.ToString();
    }
}

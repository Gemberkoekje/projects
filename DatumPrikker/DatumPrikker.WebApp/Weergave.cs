using System.Globalization;

namespace DatumPrikker.WebApp;

/// <summary>How dates and choices read on the page, in Dutch.</summary>
public static class Weergave
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>The short day, such as "za 14 dec".</summary>
    /// <param name="option">The date option.</param>
    /// <returns>The text.</returns>
    public static string Dag(DateOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option.Date.ToString("ddd d MMM", Nl);
    }

    /// <summary>The long day, such as "zaterdag 14 december 2026".</summary>
    /// <param name="option">The date option.</param>
    /// <returns>The text.</returns>
    public static string DagLang(DateOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option.Date.ToString("dddd d MMMM yyyy", Nl);
    }

    /// <summary>The time range, such as "19:00–22:00", or "hele dag".</summary>
    /// <param name="option">The date option.</param>
    /// <returns>The text.</returns>
    public static string Tijd(DateOption option)
    {
        ArgumentNullException.ThrowIfNull(option);

        return option.HasTime
            ? $"{option.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}–{option.End.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : "hele dag";
    }

    /// <summary>The word for a choice.</summary>
    /// <param name="availability">The choice.</param>
    /// <returns>Ja, Misschien, Nee, or a dash.</returns>
    public static string Woord(Availability availability) => availability switch
    {
        Availability.Yes => "Ja",
        Availability.Maybe => "Misschien",
        Availability.No => "Nee",
        _ => "–",
    };

    /// <summary>The CSS class for a choice.</summary>
    /// <param name="availability">The choice.</param>
    /// <returns>The class name.</returns>
    public static string Klasse(Availability availability) => availability switch
    {
        Availability.Yes => "ja",
        Availability.Maybe => "misschien",
        Availability.No => "nee",
        _ => "geen",
    };

    /// <summary>The choices a respondent can make, in the order they are offered.</summary>
    public static IReadOnlyList<Availability> Keuzes { get; } = [Availability.Yes, Availability.Maybe, Availability.No];

    /// <summary>The span of a poll's dates, such as "12 dec", "12 – 14 dec" or "30 nov – 2 dec".</summary>
    /// <param name="poll">The poll.</param>
    /// <returns>The text; empty for a poll without dates.</returns>
    public static string Bereik(Poll poll)
    {
        ArgumentNullException.ThrowIfNull(poll);

        if (poll.Options.Count == 0)
        {
            return string.Empty;
        }

        var first = poll.Options.Min(option => option.Date);
        var last = poll.Options.Max(option => option.Date);
        if (first == last)
        {
            return first.ToString("d MMM", Nl);
        }

        return first.Month == last.Month && first.Year == last.Year
            ? $"{first.ToString("%d", Nl)} – {last.ToString("d MMM", Nl)}"
            : $"{first.ToString("d MMM", Nl)} – {last.ToString("d MMM", Nl)}";
    }

    /// <summary>A moment in Dutch local time, such as "9 november om 20:15".</summary>
    /// <param name="utc">The moment in UTC.</param>
    /// <returns>The text.</returns>
    public static string Moment(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Amsterdam).ToString("d MMMM 'om' HH:mm", Nl);

    /// <summary>How many dates got each choice, such as "2× ja, 1× misschien".</summary>
    /// <param name="answer">The answer.</param>
    /// <returns>The text, leaving out choices nobody picked.</returns>
    public static string Samenvatting(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var parts = Keuzes
            .Select(keuze => (Keuze: keuze, Aantal: answer.Choices.Count(choice => choice.Availability == keuze)))
            .Where(part => part.Aantal > 0)
            .Select(part => $"{part.Aantal}× {Woord(part.Keuze).ToLowerInvariant()}");
        return string.Join(", ", parts);
    }

    private static readonly TimeZoneInfo Amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
}

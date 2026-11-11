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

    /// <summary>The short month for a calendar tile, such as "dec".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text, without a trailing dot.</returns>
    public static string Maand(DateOnly date) => date.ToString("MMM", Nl).TrimEnd('.');

    /// <summary>The day of the month for a calendar tile, such as "14".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    public static string DagNummer(DateOnly date) => date.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>The weekday for a calendar tile, such as "zaterdag".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    public static string Weekdag(DateOnly date) => date.ToString("dddd", Nl);

    /// <summary>The short weekday, such as "za".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    public static string WeekdagKort(DateOnly date) => date.ToString("ddd", Nl).TrimEnd('.');

    /// <summary>A share of a total as a CSS percentage, such as "66.7%"; 0% when the total is 0.</summary>
    /// <param name="part">The part.</param>
    /// <param name="total">The total.</param>
    /// <returns>The percentage, with a dot as decimal separator whatever the culture.</returns>
    public static string Procent(int part, int total)
        => total <= 0 ? "0%" : (100.0 * part / total).ToString("0.#", CultureInfo.InvariantCulture) + "%";

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
    public static string Moment(DateTime utc) => DutchTime.FromUtc(utc).ToString("d MMMM 'om' HH:mm", Nl);

    /// <summary>A day in Dutch local time, such as "9 nov 2026".</summary>
    /// <param name="utc">The moment in UTC.</param>
    /// <returns>The text.</returns>
    public static string Datum(DateTime utc) => DutchTime.FromUtc(utc).ToString("d MMM yyyy", Nl);

    /// <summary>The word for a refused access state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>"afgewezen", "ingetrokken", or the plain state otherwise.</returns>
    public static string Stand(AccessState state) => state switch
    {
        AccessState.Requested => "aangevraagd",
        AccessState.Approved => "toegestaan",
        AccessState.Declined => "afgewezen",
        AccessState.Revoked => "ingetrokken",
        _ => "geen",
    };

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

    /// <summary>A date's counts, such as "5× ja, 1× misschien"; Misschien only when somebody said it.</summary>
    /// <param name="tally">The date's tally.</param>
    /// <returns>The text.</returns>
    public static string Telling(OptionTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);

        return tally.Maybe > 0 ? $"{tally.Yes}× ja, {tally.Maybe}× misschien" : $"{tally.Yes}× ja";
    }

    /// <summary>
    /// The line under the most likely date (D21): how it was chosen, from how many answers, and that
    /// the maker decides. For example: "Meeste ja, dan meeste misschien, uit 7 antwoorden. Nog niet
    /// definitief: dat beslist Arie".
    /// </summary>
    /// <param name="likely">The likely date; it should have at least one leading date.</param>
    /// <param name="owner">The maker's name, who decides.</param>
    /// <returns>The text.</returns>
    public static string Vermoedelijk(LikelyDate likely, string owner)
    {
        ArgumentNullException.ThrowIfNull(likely);

        var uit = $"uit {likely.AnswerCount} {(likely.AnswerCount == 1 ? "antwoord" : "antwoorden")}";
        var hoe = likely.IsTie
            ? $"Deze {likely.Leading.Count} staan gelijk: evenveel ja en misschien, {uit}."
            : $"Meeste ja, dan meeste misschien, {uit}.";
        return $"{hoe} Nog niet definitief: dat beslist {owner}.";
    }

    /// <summary>A day with its weekday, such as "zaterdag 12 december".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    public static string DagMaand(DateOnly date) => date.ToString("dddd d MMMM", Nl);

    /// <summary>A day with its weekday and short month, such as "zaterdag 12 dec".</summary>
    /// <param name="date">The day.</param>
    /// <returns>The text.</returns>
    public static string DagMaandKort(DateOnly date) => $"{Weekdag(date)} {DagNummer(date)} {Maand(date)}";

    /// <summary>
    /// The poll's state for its label: "gekozen" once a date is picked, otherwise "open" or "gesloten"
    /// (D22: a passed deadline counts as closed).
    /// </summary>
    /// <param name="poll">The poll.</param>
    /// <param name="isOpen">True while it takes answers.</param>
    /// <returns>The word.</returns>
    public static string Staat(Poll poll, bool isOpen)
    {
        ArgumentNullException.ThrowIfNull(poll);

        if (poll.HasPick)
        {
            return "gekozen";
        }

        return isOpen ? "open" : "gesloten";
    }

    /// <summary>The CSS class of the label that goes with <see cref="Staat"/>.</summary>
    /// <param name="poll">The poll.</param>
    /// <param name="isOpen">True while it takes answers.</param>
    /// <returns>The class name.</returns>
    public static string StaatKlasse(Poll poll, bool isOpen)
    {
        ArgumentNullException.ThrowIfNull(poll);

        if (poll.HasPick)
        {
            return "label-gekozen";
        }

        return isOpen ? "label-open" : "label-dicht";
    }

    /// <summary>
    /// The answer page's deadline (D22): "Antwoorden kan tot vrijdag 4 december om 18:00", or for a
    /// deadline without a time "Antwoorden kan tot en met vrijdag 4 december".
    /// </summary>
    /// <param name="poll">A poll with a deadline.</param>
    /// <returns>The text.</returns>
    public static string AntwoordenTot(Poll poll)
    {
        var dag = SluitDag(poll, out var tijd);
        return tijd.Length > 0 ? $"Antwoorden kan tot {DagMaand(dag)} om {tijd}" : $"Antwoorden kan tot en met {DagMaand(dag)}";
    }

    /// <summary>
    /// The results' deadline line (D22): "Sluit vanzelf op vrijdag 4 december om 18:00." before it,
    /// "Vanzelf gesloten op …" after it. Without a time: "Sluit vanzelf na vrijdag 4 december.".
    /// </summary>
    /// <param name="poll">A poll with a deadline.</param>
    /// <param name="passed">True once the deadline closed it.</param>
    /// <returns>The text.</returns>
    public static string SluitVanzelf(Poll poll, bool passed)
    {
        var wat = passed ? "Vanzelf gesloten" : "Sluit vanzelf";
        var dag = SluitDag(poll, out var tijd);
        return tijd.Length > 0 ? $"{wat} op {DagMaand(dag)} om {tijd}." : $"{wat} na {DagMaand(dag)}.";
    }

    /// <summary>The deadline in a list (D22): "sluit 18 december om 12:00", or "sluit na 18 december".</summary>
    /// <param name="poll">A poll with a deadline.</param>
    /// <returns>The text.</returns>
    public static string SluitKort(Poll poll)
    {
        var dag = SluitDag(poll, out var tijd).ToString("d MMMM", Nl);
        return tijd.Length > 0 ? $"sluit {dag} om {tijd}" : $"sluit na {dag}";
    }

    /// <summary>The question before picking a date (D22), for the browser's confirm.</summary>
    /// <param name="option">The date to pick.</param>
    /// <param name="closes">True when picking also closes the poll.</param>
    /// <returns>The text.</returns>
    public static string KiesVraag(DateOption option, bool closes)
    {
        ArgumentNullException.ThrowIfNull(option);

        var gevolg = closes ? "Dan sluit de prikker en ziet iedereen met de link deze datum." : "Dan ziet iedereen met de link deze datum.";
        return $"Dit wordt het: {DagMaand(option.Date)}, {Tijd(option)}? {gevolg}";
    }

    /// <summary>A file name for a poll's agenda file, from its title, such as "etentje-met-de-buren.ics".</summary>
    /// <param name="title">The poll's title.</param>
    /// <returns>The file name; "afspraak.ics" when the title has no plain letters or digits.</returns>
    public static string AgendaBestand(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var slug = new string([.. title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]);
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 60)
        {
            slug = slug[..60].TrimEnd('-');
        }

        return slug.Length == 0 ? "afspraak.ics" : $"{slug}.ics";
    }

    /// <summary>The local day of the deadline, and its time ("18:00"); an empty time for the end of the day.</summary>
    private static DateOnly SluitDag(Poll poll, out string tijd)
    {
        ArgumentNullException.ThrowIfNull(poll);

        if (poll.DeadlineHasTime)
        {
            var local = DutchTime.FromUtc(poll.Deadline);
            tijd = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            return DateOnly.FromDateTime(local);
        }

        // The end of a day is the next day's midnight: step back one tick to land on the day itself.
        tijd = string.Empty;
        return DateOnly.FromDateTime(DutchTime.FromUtc(poll.Deadline.AddTicks(-1)));
    }
}

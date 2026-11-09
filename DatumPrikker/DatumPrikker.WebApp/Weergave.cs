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
}

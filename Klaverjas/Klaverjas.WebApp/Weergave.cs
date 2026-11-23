using System.Globalization;

namespace Klaverjas.WebApp;

/// <summary>Dates and numbers the Dutch way, as on Dad's sheets.</summary>
public static class Weergave
{
    /// <summary>Dutch formatting.</summary>
    public static CultureInfo Nl { get; } = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>"Maandag 16 november 2026".</summary>
    /// <param name="datum">The date.</param>
    /// <returns>The long form, capitalised.</returns>
    public static string Lang(DateOnly datum) => Hoofdletter(InZin(datum));

    /// <summary>"maandag 16 november 2026", for the middle of a sentence.</summary>
    /// <param name="datum">The date.</param>
    /// <returns>The long form, lower case.</returns>
    public static string InZin(DateOnly datum) => datum.ToString("dddd d MMMM yyyy", Nl);

    /// <summary>"Ma 16 nov".</summary>
    /// <param name="datum">The date.</param>
    /// <returns>The short form, capitalised.</returns>
    public static string Kort(DateOnly datum) => Hoofdletter(datum.ToString("ddd d MMM", Nl).Replace(".", string.Empty, StringComparison.Ordinal));

    /// <summary>"16 nov", for a column header.</summary>
    /// <param name="datum">The date.</param>
    /// <returns>Day and month.</returns>
    public static string DagMaand(DateOnly datum) => datum.ToString("d MMM", Nl).Replace(".", string.Empty, StringComparison.Ordinal);

    /// <summary>"November".</summary>
    /// <param name="datum">A date in the month.</param>
    /// <returns>The month's name, capitalised.</returns>
    public static string Maand(DateOnly datum) => Hoofdletter(datum.ToString("MMMM", Nl));

    /// <summary>The date as <c>yyyy-MM-dd</c>, for links and date fields.</summary>
    /// <param name="datum">The date.</param>
    /// <returns>The ISO form.</returns>
    public static string Iso(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A whole number without thousands separators, as on the sheet ("198883").</summary>
    /// <param name="getal">The number.</param>
    /// <returns>The digits.</returns>
    public static string Getal(int getal) => getal.ToString(CultureInfo.InvariantCulture);

    /// <summary>The Dutch name of a weekday ("maandag").</summary>
    /// <param name="dag">The weekday.</param>
    /// <returns>Its name.</returns>
    public static string Dag(DayOfWeek dag) => Nl.DateTimeFormat.GetDayName(dag);

    /// <summary>The CSS class of an evening's top-3 place.</summary>
    /// <param name="plaats">The place.</param>
    /// <returns><c>c1</c>, <c>c2</c>, <c>c3</c>, or empty.</returns>
    public static string PlaatsKlasse(int plaats) => plaats switch
    {
        1 => "c1",
        2 => "c2",
        3 => "c3",
        _ => string.Empty,
    };

    private static string Hoofdletter(string tekst)
        => tekst.Length == 0 ? tekst : char.ToUpper(tekst[0], Nl) + tekst[1..];
}

using System.Globalization;

namespace Kluskat.WebApp.Pages.Beheer;

/// <summary>Small helpers for the beheer forms.</summary>
internal static class Formulieren
{
    /// <summary>Parses the value of an <c>&lt;input type="date"&gt;</c>.</summary>
    /// <param name="waarde">The posted value, <c>yyyy-MM-dd</c> or empty.</param>
    /// <returns>The date, or <see langword="default"/> when empty or unreadable.</returns>
    internal static DateOnly Datum(string waarde)
        => DateOnly.TryParseExact(waarde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var datum)
            ? datum
            : default;

    /// <summary>The size options, without the empty one.</summary>
    internal static IReadOnlyList<Grootte> Groottes { get; } = [Grootte.Klein, Grootte.Middel, Grootte.Groot];
}

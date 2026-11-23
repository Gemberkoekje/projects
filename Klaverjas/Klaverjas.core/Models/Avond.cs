using System.Globalization;

namespace Klaverjas.core.Models;

/// <summary>One evening's results. One document per date; saving it again keeps the old one (D8).</summary>
public sealed record Avond
{
    /// <summary>The document identity: the date as <c>yyyy-MM-dd</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>One result per player who was on the entry screen.</summary>
    public IReadOnlyList<Uitslag> Uitslagen { get; init; } = [];

    /// <summary>The document identity of an evening.</summary>
    /// <param name="datum">The evening.</param>
    /// <returns>The date as <c>yyyy-MM-dd</c>.</returns>
    public static string IdVan(DateOnly datum) => datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A new evening.</summary>
    /// <param name="datum">The evening.</param>
    /// <param name="uitslagen">The results.</param>
    /// <returns>The evening with its identity filled in.</returns>
    public static Avond Van(DateOnly datum, IEnumerable<Uitslag> uitslagen)
        => new Avond { Id = IdVan(datum), Datum = datum, Uitslagen = [.. uitslagen] };
}

namespace Klaverjas.application.Import;

/// <summary>
/// What Dad's "Standen" workbook holds, as read: the `Stand` sheet (points) and the `pit` sheet
/// (pits), with the workbook's own totals kept for the comparison (D4). Nothing here is interpreted
/// yet; <see cref="ImportPlanner"/> does that.
/// </summary>
public sealed record StandenBestand
{
    /// <summary>The season the sheet is about ("Punten 2026").</summary>
    public int Jaar { get; init; }

    /// <summary>The club days with at least one score, in order.</summary>
    public IReadOnlyList<DateOnly> Avonden { get; init; } = [];

    /// <summary>The club days with text instead of scores ("Pasen").</summary>
    public IReadOnlyList<GeenSpeelavond> GeenSpeelavonden { get; init; } = [];

    /// <summary>The `Stand` rows: a name, the points per evening and the sheet's own total.</summary>
    public IReadOnlyList<BestandsRegel> Punten { get; init; } = [];

    /// <summary>The `pit` rows: a name (spelled as on that sheet), the pits per evening and the sheet's own total.</summary>
    public IReadOnlyList<BestandsRegel> Pitten { get; init; } = [];

    /// <summary>The `pit` sheet's "Week totalen" row, as the sheet computed it.</summary>
    public IReadOnlyList<DatumWaarde> WeekTotalenPitten { get; init; } = [];

    /// <summary>The `Stand` sheet's "invallers" row: substitutes' scores, without names.</summary>
    public IReadOnlyList<DatumWaarde> Invallers { get; init; } = [];
}

/// <summary>One row of a sheet.</summary>
public sealed record BestandsRegel
{
    /// <summary>The name as typed on that sheet (trimmed).</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>The numbers per evening; evenings without a number are absent from the list.</summary>
    public IReadOnlyList<DatumWaarde> Waarden { get; init; } = [];

    /// <summary>The row's total as the sheet computed it.</summary>
    public int Totaal { get; init; }

    /// <summary>The number of an evening.</summary>
    /// <param name="datum">The evening.</param>
    /// <param name="getal">The number, when there is one.</param>
    /// <returns><see langword="true"/> when the row has a number for that evening.</returns>
    public bool Op(DateOnly datum, out int getal)
    {
        var waarde = Waarden.FirstOrDefault(w => w.Datum == datum);
        getal = waarde?.Getal ?? 0;
        return waarde is not null;
    }
}

/// <summary>A number on a date. A list of these instead of a dictionary, so the document stores plainly.</summary>
public sealed record DatumWaarde
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>The number.</summary>
    public int Getal { get; init; }
}

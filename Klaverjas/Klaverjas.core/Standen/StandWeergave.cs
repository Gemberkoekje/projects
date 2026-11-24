namespace Klaverjas.core.Standen;

/// <summary>
/// The standings as the players see them (D5): names and numbers only, with the latest evening's
/// month as columns (R8). What "Zet de stand online" puts online is one of these, frozen (D2).
/// </summary>
public sealed record StandWeergave
{
    /// <summary>The season.</summary>
    public int Jaar { get; init; }

    /// <summary>The latest entered evening; <see cref="DateOnly.MinValue"/> when there is none yet.</summary>
    public DateOnly LaatsteAvond { get; init; } = DateOnly.MinValue;

    /// <summary>Whether the season has no entered evening yet.</summary>
    public bool IsLeeg => LaatsteAvond == DateOnly.MinValue;

    /// <summary>The month columns: the club days of the latest evening's month.</summary>
    public IReadOnlyList<Kolom> Kolommen { get; init; } = [];

    /// <summary>The latest evening's top 3.</summary>
    public IReadOnlyList<WeergaveWinnaar> Winnaars { get; init; } = [];

    /// <summary>The members by place on points.</summary>
    public IReadOnlyList<WeergaveRegel> Punten { get; init; } = [];

    /// <summary>The members by place on pits; the cells hold pits.</summary>
    public IReadOnlyList<WeergaveRegel> Pitten { get; init; } = [];

    /// <summary>The substitutes who played in the month, by name; never ranked (R4).</summary>
    public IReadOnlyList<WeergaveRegel> Invallers { get; init; } = [];

    /// <summary>The column of the latest evening (the phone shows only that one); -1 when empty.</summary>
    public int LaatsteKolom => Kolommen.Select(k => k.Datum).ToList().IndexOf(LaatsteAvond);

    /// <summary>Builds what the players see from the computed standings.</summary>
    /// <param name="stand">The standings.</param>
    /// <param name="instellingen">The play day and the evenings without play.</param>
    /// <returns>The standings as shown.</returns>
    public static StandWeergave Van(Stand stand, Instellingen instellingen)
    {
        ArgumentNullException.ThrowIfNull(stand);
        ArgumentNullException.ThrowIfNull(instellingen);

        if (stand.IsLeeg)
        {
            return new StandWeergave { Jaar = stand.Jaar };
        }

        var laatste = stand.LaatsteAvond;

        // The club days of the month, plus any evening entered on another day, so the latest evening
        // always has its column.
        var dagen = Kalender.Speeldagen(laatste.Year, laatste.Month, instellingen.Speeldag)
            .Concat(stand.Avonden.Where(d => d.Year == laatste.Year && d.Month == laatste.Month))
            .Distinct()
            .Order()
            .ToList();

        var kolommen = dagen.Select(d => new Kolom
        {
            Datum = d,
            Vrij = instellingen.GeenSpeelavonden.Where(g => g.Datum == d).Select(g => g.Reden).FirstOrDefault() ?? string.Empty,
            IsGespeeld = stand.Avonden.Contains(d),
            PittenTotaal = stand.WeekTotalenPitten.TryGetValue(d, out var totaal) ? totaal : 0,
        }).ToList();

        return new StandWeergave
        {
            Jaar = stand.Jaar,
            LaatsteAvond = laatste,
            Kolommen = kolommen,
            Winnaars = [.. stand.Winnaars.Select(w => new WeergaveWinnaar { Naam = w.Speler.Naam, Plaats = w.Plaats, Punten = w.Punten, IsInvaller = !w.Speler.IsLid })],
            Punten = [.. stand.Punten.Select(r => PuntenRegel(r, dagen))],
            Pitten = [.. stand.Pitten.Select(r => PittenRegel(r, dagen))],
            Invallers = [.. stand.Invallers.Where(r => dagen.Exists(d => !r.Op(d).IsLeeg)).Select(r => PuntenRegel(r, dagen))],
        };
    }

    private static WeergaveRegel PuntenRegel(StandRegel regel, List<DateOnly> dagen) => new WeergaveRegel
    {
        Plaats = regel.Plaats,
        Naam = regel.Speler.Naam,
        Totaal = regel.Totaal,
        Verschil = regel.Verschil,
        Cellen = [.. dagen.Select(regel.Op).Select(c => new WeergaveCel { Status = c.Status, Waarde = c.Punten, Plaats = c.AvondPlaats })],
    };

    private static WeergaveRegel PittenRegel(StandRegel regel, List<DateOnly> dagen) => new WeergaveRegel
    {
        Plaats = regel.PlaatsPitten,
        Naam = regel.Speler.Naam,
        Totaal = regel.Pitten,
        Cellen = [.. dagen.Select(regel.Op).Select(c => new WeergaveCel { Status = c.Status, Waarde = c.Pitten })],
    };
}

/// <summary>One month column: a club day, perhaps without play.</summary>
public sealed record Kolom
{
    /// <summary>The date.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>Why there is no play, such as "Pasen"; empty on a normal club day.</summary>
    public string Vrij { get; init; } = string.Empty;

    /// <summary>Whether this is an evening without play.</summary>
    public bool IsVrij => Vrij.Length > 0;

    /// <summary>Whether the evening has been entered.</summary>
    public bool IsGespeeld { get; init; }

    /// <summary>"Week totalen": the members' pits that evening (R7).</summary>
    public int PittenTotaal { get; init; }
}

/// <summary>One row as the players see it.</summary>
public sealed record WeergaveRegel
{
    /// <summary>The place (on points or on pits); 0 for a substitute.</summary>
    public int Plaats { get; init; }

    /// <summary>The full name, as on the PDF (D5).</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>The season's points or pits.</summary>
    public int Totaal { get; init; }

    /// <summary>The gap to the row above (points only, R6).</summary>
    public int Verschil { get; init; }

    /// <summary>One cell per month column.</summary>
    public IReadOnlyList<WeergaveCel> Cellen { get; init; } = [];

    /// <summary>The cell of a month column.</summary>
    /// <param name="kolom">The column's index.</param>
    /// <returns>The cell, or an empty one outside the columns.</returns>
    public WeergaveCel Cel(int kolom) => kolom >= 0 && kolom < Cellen.Count ? Cellen[kolom] : new WeergaveCel();
}

/// <summary>One cell as the players see it.</summary>
public sealed record WeergaveCel
{
    /// <summary>How the player took part; <see cref="Status.None"/> for an empty cell.</summary>
    public Status Status { get; init; }

    /// <summary>The points (the absence score when absent) or the pits.</summary>
    public int Waarde { get; init; }

    /// <summary>1, 2 or 3 among the evening's top 3 on points (the cell's colour); otherwise 0.</summary>
    public int Plaats { get; init; }

    /// <summary>Whether nothing counts for this evening.</summary>
    public bool IsLeeg => Status == Status.None;

    /// <summary>Whether this is the absence score (green, R3).</summary>
    public bool IsAfwezig => Status == Status.Afwezig;
}

/// <summary>One of the evening's top 3.</summary>
public sealed record WeergaveWinnaar
{
    /// <summary>The full name.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>1, 2 or 3; ties share a place.</summary>
    public int Plaats { get; init; }

    /// <summary>The evening's points.</summary>
    public int Punten { get; init; }

    /// <summary>Whether a substitute won a place (R4).</summary>
    public bool IsInvaller { get; init; }
}

namespace Klaverjas.core.Standen;

/// <summary>One player's score for one evening, as one cell of the standings.</summary>
public sealed record Cel
{
    /// <summary>An evening without a score for this player.</summary>
    public static readonly Cel Leeg = new Cel();

    /// <summary>How the player took part; <see cref="Status.None"/> for an empty cell.</summary>
    public Status Status { get; init; }

    /// <summary>The points that count: the card total, or the absence score.</summary>
    public int Punten { get; init; }

    /// <summary>The pits (0 when absent).</summary>
    public int Pitten { get; init; }

    /// <summary>1, 2 or 3 when among the evening's top 3 (R4, R5); otherwise 0.</summary>
    public int AvondPlaats { get; init; }

    /// <summary>Whether nothing counts for this evening.</summary>
    public bool IsLeeg => Status == Status.None;

    /// <summary>Whether this is the absence score (green, R3).</summary>
    public bool IsAfwezig => Status == Status.Afwezig;
}

/// <summary>A member's lowest evening of a quarter: marked, never subtracted (R16).</summary>
public sealed record LaagsteAvond
{
    /// <summary>The quarter (1–4).</summary>
    public int Kwartaal { get; init; }

    /// <summary>The first evening with the lowest score.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>The score.</summary>
    public int Punten { get; init; }
}

/// <summary>One row of the standings.</summary>
public sealed record StandRegel
{
    /// <summary>Who.</summary>
    public Speler Speler { get; init; } = new Speler();

    /// <summary>The place on points; ties share a place (R5). 0 for a substitute.</summary>
    public int Plaats { get; init; }

    /// <summary>The sum of every evening of the season (R1, R16).</summary>
    public int Totaal { get; init; }

    /// <summary>The gap to the row directly above; 0 for the top row (R6).</summary>
    public int Verschil { get; init; }

    /// <summary>The place on pits, with the same tie rule (R7). 0 for a substitute.</summary>
    public int PlaatsPitten { get; init; }

    /// <summary>The season's pits.</summary>
    public int Pitten { get; init; }

    /// <summary>The cells of the season's evenings; evenings without a cell are empty.</summary>
    public IReadOnlyDictionary<DateOnly, Cel> Avonden { get; init; } = new Dictionary<DateOnly, Cel>();

    /// <summary>The lowest evening per quarter that has evenings (R16).</summary>
    public IReadOnlyList<LaagsteAvond> Laagste { get; init; } = [];

    /// <summary>The cell of an evening.</summary>
    /// <param name="avond">The evening.</param>
    /// <returns>The cell, or <see cref="Cel.Leeg"/>.</returns>
    public Cel Op(DateOnly avond) => Avonden.TryGetValue(avond, out var cel) ? cel : Cel.Leeg;
}

/// <summary>One of the evening's top 3 (R4: a substitute can be one).</summary>
public sealed record Winnaar
{
    /// <summary>Who.</summary>
    public Speler Speler { get; init; } = new Speler();

    /// <summary>1, 2 or 3; ties share a place.</summary>
    public int Plaats { get; init; }

    /// <summary>The evening's points.</summary>
    public int Punten { get; init; }
}

/// <summary>The standings of one season after its latest entered evening. Computed, never stored (D8).</summary>
public sealed record Stand
{
    /// <summary>The season (a calendar year, R1).</summary>
    public int Jaar { get; init; }

    /// <summary>The latest entered evening; <see cref="DateOnly.MinValue"/> when there is none yet.</summary>
    public DateOnly LaatsteAvond { get; init; } = DateOnly.MinValue;

    /// <summary>Whether the season has no entered evening yet.</summary>
    public bool IsLeeg => LaatsteAvond == DateOnly.MinValue;

    /// <summary>The season's entered evenings, in order.</summary>
    public IReadOnlyList<DateOnly> Avonden { get; init; } = [];

    /// <summary>The members in the stand, by place on points.</summary>
    public IReadOnlyList<StandRegel> Punten { get; init; } = [];

    /// <summary>The same members, by place on pits.</summary>
    public IReadOnlyList<StandRegel> Pitten { get; init; } = [];

    /// <summary>The substitutes who played this season, by name; never ranked (R4).</summary>
    public IReadOnlyList<StandRegel> Invallers { get; init; } = [];

    /// <summary>The latest evening's top 3.</summary>
    public IReadOnlyList<Winnaar> Winnaars { get; init; } = [];

    /// <summary>"Week totalen": the members' pits per evening; substitutes don't count (R7).</summary>
    public IReadOnlyDictionary<DateOnly, int> WeekTotalenPitten { get; init; } = new Dictionary<DateOnly, int>();

    /// <summary>The standings of a season without evenings.</summary>
    /// <param name="jaar">The season.</param>
    /// <returns>An empty stand.</returns>
    public static Stand Leeg(int jaar) => new Stand { Jaar = jaar };
}

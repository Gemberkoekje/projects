namespace Klaverjas.application.ReadModels;

/// <summary>The entry screen of one evening (D1).</summary>
public sealed record UitslagFormulier
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>Whether the evening has not been entered before.</summary>
    public bool IsNieuw { get; init; }

    /// <summary>The members' rows, by name.</summary>
    public IReadOnlyList<UitslagRegel> Leden { get; init; } = [];

    /// <summary>The substitutes' rows, by name.</summary>
    public IReadOnlyList<UitslagRegel> Invallers { get; init; } = [];

    /// <summary>The substitutes not on the evening yet, for "Invaller toevoegen".</summary>
    public IReadOnlyList<Speler> AndereInvallers { get; init; } = [];

    /// <summary>The absence score, shown in the absentees' rows.</summary>
    public int PuntenAfwezig { get; init; }
}

/// <summary>One row of the entry screen, with the numbers as text so a half-typed row survives a round trip.</summary>
public sealed record UitslagRegel
{
    /// <summary>Who.</summary>
    public Speler Speler { get; init; } = new Speler();

    /// <summary>Played, sat out a round, or absent.</summary>
    public Status Status { get; init; } = Status.Speelt;

    /// <summary>The card total as typed.</summary>
    public string Punten { get; init; } = string.Empty;

    /// <summary>The pits as typed.</summary>
    public string Pitten { get; init; } = string.Empty;

    /// <summary>The round a slaper sat out.</summary>
    public int Ronde { get; init; }
}

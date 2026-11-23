namespace Klaverjas.application.Commands;

/// <summary>Saves an evening's results from the entry screen ("Opslaan en stand bekijken").</summary>
public sealed record UitslagOpslaan : ICommand
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>One row per player on the screen.</summary>
    public IReadOnlyList<UitslagInvoer> Regels { get; init; } = [];
}

/// <summary>One row as typed: the numbers stay text until the handler has checked them.</summary>
public sealed record UitslagInvoer
{
    /// <summary>Who.</summary>
    public Guid SpelerId { get; init; }

    /// <summary>Played, sat out a round, or absent.</summary>
    public Status Status { get; init; }

    /// <summary>The card total as typed.</summary>
    public string Punten { get; init; } = string.Empty;

    /// <summary>The pits as typed; empty counts as 0.</summary>
    public string Pitten { get; init; } = string.Empty;

    /// <summary>The round a slaper sat out.</summary>
    public int Ronde { get; init; }
}

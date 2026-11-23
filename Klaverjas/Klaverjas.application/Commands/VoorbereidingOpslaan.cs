namespace Klaverjas.application.Commands;

/// <summary>Saves the "Kaartjes en tafelindeling" screen, optionally filling the empty slaper places fairly.</summary>
public sealed record VoorbereidingOpslaan : ICommand
{
    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>The members who get a card.</summary>
    public IReadOnlyList<Guid> Kaartjes { get; init; } = [];

    /// <summary>Blank cards for substitutes.</summary>
    public int Blanco { get; init; }

    /// <summary>The expected number of players.</summary>
    public int Verwacht { get; init; }

    /// <summary>The slapers by number; <see cref="Guid.Empty"/> for an empty place.</summary>
    public IReadOnlyList<Guid> Slapers { get; init; } = [];

    /// <summary>"Eerlijk voorstel": fill the empty places with who sat out least.</summary>
    public bool Voorstellen { get; init; }
}

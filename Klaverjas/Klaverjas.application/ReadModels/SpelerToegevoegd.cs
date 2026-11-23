namespace Klaverjas.application.ReadModels;

/// <summary>What adding a player did, for the confirmation on the Spelers screen.</summary>
public sealed record SpelerToegevoegd
{
    /// <summary>The new player.</summary>
    public Speler Speler { get; init; } = new Speler();

    /// <summary>For a new member: the season's evenings so far, which count as absent (R17).</summary>
    public int GemisteAvonden { get; init; }
}

namespace Klaverjas.application.Commands;

/// <summary>Adds a member or a substitute, from the Spelers screen.</summary>
public sealed record SpelerToevoegen : ICommand<SpelerToegevoegd>
{
    /// <summary>First name and surname.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>Member or substitute.</summary>
    public Soort Soort { get; init; }

    /// <summary>Email address; may be empty.</summary>
    public string Email { get; init; } = string.Empty;
}

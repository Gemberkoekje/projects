namespace Klaverjas.application.Commands;

/// <summary>Changes a player, from "Wijzigen" on the Spelers screen.</summary>
public sealed record SpelerAanpassen : ICommand
{
    /// <summary>Who.</summary>
    public Guid Id { get; init; }

    /// <summary>First name and surname.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>Member or substitute.</summary>
    public Soort Soort { get; init; }

    /// <summary>Email address; may be empty.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Phone number; may be empty.</summary>
    public string Telefoon { get; init; } = string.Empty;

    /// <summary>Postal address; may be empty.</summary>
    public string Adres { get; init; } = string.Empty;

    /// <summary>Birthday; may be empty.</summary>
    public string Verjaardag { get; init; } = string.Empty;

    /// <summary><see langword="false"/> for "Gestopt".</summary>
    public bool DoetMee { get; init; } = true;

    /// <summary>Whether the member stays in this season's rankings.</summary>
    public bool InStand { get; init; } = true;
}

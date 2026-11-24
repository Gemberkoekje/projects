namespace Klaverjas.core.Models;

/// <summary>
/// A member or a substitute (D14). Results refer to the player by <see cref="Id"/>, so a corrected
/// name shows everywhere at once, in old weeks too (R9).
/// </summary>
public sealed record Speler
{
    /// <summary>The longest name accepted.</summary>
    public const int MaxNaam = 80;

    /// <summary>The document identity.</summary>
    public Guid Id { get; init; }

    /// <summary>First name and surname, as on the standings.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>Member or substitute.</summary>
    public Soort Soort { get; init; }

    /// <summary>Email address; empty when unknown.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Phone number; empty when unknown.</summary>
    public string Telefoon { get; init; } = string.Empty;

    /// <summary>Postal address; empty when unknown.</summary>
    public string Adres { get; init; } = string.Empty;

    /// <summary>Birthday as Dad writes it ("5 februari"); empty when unknown.</summary>
    public string Verjaardag { get; init; } = string.Empty;

    /// <summary><see langword="false"/> once the player has stopped ("Gestopt", D11).</summary>
    public bool DoetMee { get; init; } = true;

    /// <summary>
    /// Whether a member counts in this season's rankings. Stopping takes a member out by default
    /// (R18); Dad can keep them in, or take anyone out, and undo it (D16). Scores are never deleted.
    /// </summary>
    public bool InStand { get; init; } = true;

    /// <summary>
    /// The day a member joined. Every evening of the season before it counts as absent (R17).
    /// <see cref="DateOnly.MinValue"/> for a member since before the season.
    /// </summary>
    public DateOnly LidVanaf { get; init; } = DateOnly.MinValue;

    /// <summary>Whether this is a member (as opposed to a substitute).</summary>
    public bool IsLid => Soort == Soort.Lid;

    /// <summary>Checks the fields Dad types.</summary>
    /// <param name="speler">The player to check.</param>
    /// <returns>OK, or what is wrong, in Dutch.</returns>
    public static Result Controleer(Speler speler)
    {
        ArgumentNullException.ThrowIfNull(speler);

        var fouten = new List<IValidationMessage>();

        if (string.IsNullOrWhiteSpace(speler.Naam))
        {
            fouten.Add(ValidationMessage.Error("Typ de naam van de speler."));
        }
        else if (speler.Naam.Trim().Length > MaxNaam)
        {
            fouten.Add(ValidationMessage.Error($"Een naam mag hoogstens {MaxNaam} tekens lang zijn."));
        }

        if (speler.Soort == Soort.None)
        {
            fouten.Add(ValidationMessage.Error("Kies of het een lid of een invaller is."));
        }

        if (!string.IsNullOrEmpty(speler.Email) && !Emailadres.IsGeldig(speler.Email))
        {
            fouten.Add(ValidationMessage.Error("Dat e-mailadres klopt niet. Typ het zoals naam@voorbeeld.nl, of laat het leeg."));
        }

        return fouten.Count == 0 ? Result.OK : Result.WithMessages(fouten);
    }
}

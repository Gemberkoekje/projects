using System.Text.RegularExpressions;

namespace Klaverjas.core.Models;

/// <summary>Everything Dad can change himself (D10, D17). One document.</summary>
public sealed record Instellingen
{
    /// <summary>The identity of the one settings document.</summary>
    public const string HuidigeId = "huidig";

    /// <summary>The document identity; always <see cref="HuidigeId"/>.</summary>
    public string Id { get; init; } = HuidigeId;

    /// <summary>The club's name, at the top of every screen.</summary>
    public string Club { get; init; } = "De Samenspelers";

    /// <summary>The fixed score for a member who skips an evening (R3).</summary>
    public int PuntenAfwezig { get; init; } = 4200;

    /// <summary>The weekday of the club evening.</summary>
    public DayOfWeek Speeldag { get; init; } = DayOfWeek.Monday;

    /// <summary>The first evening of the current season (R13).</summary>
    public DateOnly EersteAvond { get; init; } = new DateOnly(2026, 01, 12);

    /// <summary>Evenings without play, with the reason the standings show (R13).</summary>
    public IReadOnlyList<GeenSpeelavond> GeenSpeelavonden { get; init; } = [];

    /// <summary>The colours of the standings (R5).</summary>
    public Kleuren Kleuren { get; init; } = new Kleuren();

    /// <summary>Whether no play is planned on a date.</summary>
    /// <param name="datum">The date.</param>
    /// <returns><see langword="true"/> for an evening without play.</returns>
    public bool IsGeenSpeelavond(DateOnly datum) => GeenSpeelavonden.Any(g => g.Datum == datum);

    /// <summary>Checks the fields Dad types.</summary>
    /// <param name="instellingen">The settings to check.</param>
    /// <returns>OK, or what is wrong, in Dutch.</returns>
    public static Result Controleer(Instellingen instellingen)
    {
        ArgumentNullException.ThrowIfNull(instellingen);

        var fouten = new List<IValidationMessage>();

        if (string.IsNullOrWhiteSpace(instellingen.Club))
        {
            fouten.Add(ValidationMessage.Error("Typ de naam van de club."));
        }

        if (instellingen.PuntenAfwezig is < 0 or > Uitslag.MaxPunten)
        {
            fouten.Add(ValidationMessage.Error("De punten bij afwezigheid moeten een gewoon getal zijn, zoals 4200."));
        }

        if (!new[] { instellingen.Kleuren.Eerste, instellingen.Kleuren.Tweede, instellingen.Kleuren.Derde, instellingen.Kleuren.Afwezig }.All(Kleuren.IsKleur))
        {
            fouten.Add(ValidationMessage.Error("Een van de kleuren klopt niet."));
        }

        return fouten.Count == 0 ? Result.OK : Result.WithMessages(fouten);
    }
}

/// <summary>An evening without play, such as Easter Monday (R13).</summary>
public sealed record GeenSpeelavond
{
    /// <summary>The date.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>What the standings show in that week's column, such as "Pasen".</summary>
    public string Reden { get; init; } = string.Empty;
}

/// <summary>The standings' colours, as <c>#rrggbb</c> (R5). The defaults are the sheet's.</summary>
public sealed partial record Kleuren
{
    /// <summary>1st place.</summary>
    public string Eerste { get; init; } = "#ffff00";

    /// <summary>2nd place.</summary>
    public string Tweede { get; init; } = "#e9a3e4";

    /// <summary>3rd place.</summary>
    public string Derde { get; init; } = "#5bb8ec";

    /// <summary>Absent.</summary>
    public string Afwezig { get; init; } = "#92d88b";

    /// <summary>Whether a value is a colour the page can use safely.</summary>
    /// <param name="waarde">The value.</param>
    /// <returns><see langword="true"/> for <c>#rrggbb</c>.</returns>
    public static bool IsKleur(string waarde) => waarde is not null && KleurPatroon().IsMatch(waarde);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex KleurPatroon();
}

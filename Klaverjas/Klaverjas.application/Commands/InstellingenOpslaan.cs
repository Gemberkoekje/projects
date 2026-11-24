namespace Klaverjas.application.Commands;

/// <summary>Saves the Instellingen screen (D10). The evenings without play have their own commands.</summary>
public sealed record InstellingenOpslaan : ICommand
{
    /// <summary>The club's name.</summary>
    public string Club { get; init; } = string.Empty;

    /// <summary>The absence score.</summary>
    public int PuntenAfwezig { get; init; }

    /// <summary>The weekday of the club evening.</summary>
    public DayOfWeek Speeldag { get; init; }

    /// <summary>The first evening of the season.</summary>
    public DateOnly EersteAvond { get; init; }

    /// <summary>The standings' colours.</summary>
    public Kleuren Kleuren { get; init; } = new Kleuren();

    /// <summary>The players' password; empty keeps the players' page closed.</summary>
    public string Spelerswachtwoord { get; init; } = string.Empty;

    /// <summary>Dad's new password; empty keeps the current one.</summary>
    public string NieuwWachtwoord { get; init; } = string.Empty;

    /// <summary>Dad's new password typed a second time.</summary>
    public string NieuwWachtwoordHerhaald { get; init; } = string.Empty;
}

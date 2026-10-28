namespace Kluskat.WebApp;

/// <summary>Dutch display text shared by the pages.</summary>
internal static class Weergave
{
    private static readonly string[] Aanmoedigingen =
    [
        "Eentje maar.",
        "Deze kan je!",
        "Ik kijk mee.",
        "Even doen, dan is het klaar.",
        "Klein stapje.",
        "Samen lukt dit.",
        "Ik geloof in je.",
    ];

    private static readonly string[] Juichkreten =
    [
        "Lekker bezig!",
        "Prrrrrr!",
        "Kijk jou nou!",
        "Goed gedaan!",
        "Wat een held!",
        "De kat is trots.",
        "Yes!",
    ];

    /// <summary>What the cat says above a klus.</summary>
    /// <returns>A random encouragement.</returns>
    internal static string Aanmoediging() => Aanmoedigingen[Random.Shared.Next(Aanmoedigingen.Length)];

    /// <summary>What the cat says after a "Gedaan".</summary>
    /// <returns>A random cheer.</returns>
    internal static string Juich() => Juichkreten[Random.Shared.Next(Juichkreten.Length)];

    /// <summary>The size chip text.</summary>
    /// <param name="grootte">The size.</param>
    /// <returns>Roughly how long, in words.</returns>
    internal static string Grootte(Grootte grootte) => grootte switch
    {
        core.Enums.Grootte.Klein => "± 5 min",
        core.Enums.Grootte.Middel => "± 15 min",
        core.Enums.Grootte.Groot => "30+ min",
        _ => string.Empty,
    };

    /// <summary>The CSS modifier of the size chip.</summary>
    /// <param name="grootte">The size.</param>
    /// <returns>A class name.</returns>
    internal static string GrootteKlasse(Grootte grootte) => grootte switch
    {
        core.Enums.Grootte.Klein => "chip-klein",
        core.Enums.Grootte.Middel => "chip-middel",
        core.Enums.Grootte.Groot => "chip-groot",
        _ => string.Empty,
    };

    /// <summary>The size in a word, for the beheer list.</summary>
    /// <param name="grootte">The size.</param>
    /// <returns>Klein, middel or groot.</returns>
    internal static string GrootteWoord(Grootte grootte) => grootte switch
    {
        core.Enums.Grootte.Klein => "klein",
        core.Enums.Grootte.Middel => "middel",
        core.Enums.Grootte.Groot => "groot",
        _ => "?",
    };

    /// <summary>"1 klusje" or "6 klusjes".</summary>
    /// <param name="aantal">How many.</param>
    /// <returns>The count with the right noun.</returns>
    internal static string Klusjes(int aantal) => aantal == 1 ? "1 klusje" : $"{aantal} klusjes";

    /// <summary>A UTC instant as a local date, for the beheer pages.</summary>
    /// <param name="utc">The instant.</param>
    /// <returns>E.g. "28-10-2026".</returns>
    internal static string Datum(DateTime utc)
        => Kalender.Datum(utc).ToString("d-M-yyyy", System.Globalization.CultureInfo.InvariantCulture);
}

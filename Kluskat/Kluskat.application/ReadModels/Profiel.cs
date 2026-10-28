namespace Kluskat.application.ReadModels;

/// <summary>The one player's choices. Everything earned is computed; only what you pick is stored.</summary>
public sealed record Profiel
{
    /// <summary>The identity of the one profile document.</summary>
    public const string HuidigeId = "huidig";

    /// <summary>The document identity; always <see cref="HuidigeId"/>.</summary>
    public string Id { get; init; } = HuidigeId;

    /// <summary>What the cat wears; <see cref="Accessoire.None"/> for nothing.</summary>
    public Accessoire Accessoire { get; init; }
}

/// <summary>The game and the profile together, as the pages show them.</summary>
public sealed record Profieloverzicht
{
    /// <summary>The score.</summary>
    public Spel.Spelstand Spelstand { get; init; } = Spel.Spelstand.Begin;

    /// <summary>
    /// What the cat wears. Only ever an unlocked accessory: should an undo drop a level, the cat
    /// takes off what it can no longer wear rather than the page breaking.
    /// </summary>
    public Accessoire Accessoire { get; init; }

    /// <summary>How grown-up the cat is.</summary>
    public KatVorm Vorm => Spelstand.Vorm;
}

namespace Kluskat.application.ReadModels;

/// <summary>What the win screen shows after a "Gedaan".</summary>
public sealed record Overwinning
{
    /// <summary>The klus that was just done.</summary>
    public KlusOverzicht Klus { get; init; } = KlusOverzicht.Geen;

    /// <summary>What it brought: XP, a level, badges, the streak.</summary>
    public Spel.Beloning Beloning { get; init; } = new Spel.Beloning();

    /// <summary>What the cat wears, after any unlock.</summary>
    public Accessoire Accessoire { get; init; }
}

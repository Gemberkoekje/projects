namespace Kluskat.application.Kiezen;

/// <summary>What the klus screen shows.</summary>
public enum KeuzeUitkomst
{
    /// <summary>Not decided.</summary>
    None = 0,

    /// <summary>One klus to do.</summary>
    Klus = 1,

    /// <summary>Nothing is due: the cat naps.</summary>
    NiksTeDoen = 2,

    /// <summary>Something is due, but every due klus was skipped (or is too big) in this window.</summary>
    AllesOvergeslagen = 3,
}

/// <summary>The answer of <see cref="KlusKiezer"/>.</summary>
public sealed record Keuze
{
    /// <summary>What to show.</summary>
    public KeuzeUitkomst Uitkomst { get; init; }

    /// <summary>The klus to do; <see cref="KlusOverzicht.Geen"/> unless <see cref="Uitkomst"/> is <see cref="KeuzeUitkomst.Klus"/>.</summary>
    public KlusOverzicht Klus { get; init; } = KlusOverzicht.Geen;

    /// <summary>Whether this klus is not due yet: the user asked to get ahead.</summary>
    public bool Vooruit { get; init; }

    /// <summary>Whether a "Geen tijd" cap is on, so the screen can say why the big ones are hidden.</summary>
    public bool KleinGehouden { get; init; }
}

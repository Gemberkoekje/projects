using Klaverjas.core.Tafels;

namespace Klaverjas.application.ReadModels;

/// <summary>The "Kaartjes en tafelindeling" screen (D12).</summary>
public sealed record VoorbereidingFormulier
{
    /// <summary>The preparation as stored, or the defaults for a new one.</summary>
    public Voorbereiding Voorbereiding { get; init; } = new Voorbereiding();

    /// <summary>The active members, by name, with how often each sat out this season.</summary>
    public IReadOnlyList<Lid> Leden { get; init; } = [];

    /// <summary>The active substitutes, by name: in the card list, unticked unless Dad ticks one.</summary>
    public IReadOnlyList<Speler> Invallers { get; init; } = [];

    /// <summary>The table plan for the expected number of players.</summary>
    public Tafelschema Schema { get; init; } = new Tafelschema();
}

/// <summary>A member on the preparation screen.</summary>
public sealed record Lid
{
    /// <summary>Who.</summary>
    public Speler Speler { get; init; } = new Speler();

    /// <summary>How often and when last they sat out this season.</summary>
    public Slaapgeschiedenis Geslapen { get; init; } = new Slaapgeschiedenis();
}

/// <summary>One score card to print (D15).</summary>
public sealed record Kaartje
{
    /// <summary>The name on the card: the first name, or the full name when two share it; empty for a blank card.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>For a slaper, their number (printed top right); 0 otherwise.</summary>
    public int SlaperNummer { get; init; }

    /// <summary>For a slaper, the round they sit out (1600 and "Slaper n" are printed there); 0 otherwise.</summary>
    public int RondeStil { get; init; }
}

/// <summary>The score cards of one evening.</summary>
public sealed record Kaartjes
{
    /// <summary>The evening (printed on every card).</summary>
    public DateOnly Datum { get; init; }

    /// <summary>The club's name (printed on every card).</summary>
    public string Club { get; init; } = string.Empty;

    /// <summary>The cards: members by name, then the blank ones.</summary>
    public IReadOnlyList<Kaartje> Lijst { get; init; } = [];

    /// <summary>How many slapers are still to be picked (they get no card yet).</summary>
    public int OpenSlapers { get; init; }
}

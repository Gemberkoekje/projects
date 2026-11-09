namespace DatumPrikker.core.Models;

/// <summary>How one date option scored.</summary>
public sealed record OptionTally
{
    /// <summary>The date option.</summary>
    public DateOption Option { get; init; } = new DateOption();

    /// <summary>How many said Ja.</summary>
    public int Yes { get; init; }

    /// <summary>How many said Misschien.</summary>
    public int Maybe { get; init; }

    /// <summary>How many said Nee.</summary>
    public int No { get; init; }
}

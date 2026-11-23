namespace Klaverjas.core.Models;

/// <summary>One player's result of one evening: what Dad types from the score card (R2, D1).</summary>
public sealed record Uitslag
{
    /// <summary>The most pits anyone can make in an evening: every hand of three rounds of 16.</summary>
    public const int MaxPitten = 48;

    /// <summary>A ceiling far above any real card total, to catch a slip of the keyboard.</summary>
    public const int MaxPunten = 99_999;

    /// <summary>Who.</summary>
    public Guid SpelerId { get; init; }

    /// <summary>Played, sat out a round, or absent.</summary>
    public Status Status { get; init; }

    /// <summary>
    /// The card total. For a slaper it already includes the 1600 for the round sat out (R11).
    /// Ignored when absent: the absence score from the settings counts instead (R3).
    /// </summary>
    public int Punten { get; init; }

    /// <summary>The pits from the card; ignored when absent.</summary>
    public int Pitten { get; init; }

    /// <summary>The round a slaper sat out (1–3); 0 for everyone else.</summary>
    public int Ronde { get; init; }
}

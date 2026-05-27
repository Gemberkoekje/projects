namespace TheCuratool.Domain;

/// <summary>
/// Represents a character-type distribution for a game setup (Townsfolk / Outsiders / Minions / Demons).
/// </summary>
/// <param name="Townsfolk">Number of Townsfolk slots.</param>
/// <param name="Outsiders">Number of Outsider slots.</param>
/// <param name="Minions">Number of Minion slots.</param>
/// <param name="Demons">Number of Demon slots.</param>
public sealed record SetupCounts(int Townsfolk, int Outsiders, int Minions, int Demons)
{
    /// <summary>A zero-distribution sentinel value.</summary>
    public static readonly SetupCounts Empty = new(0, 0, 0, 0);
}

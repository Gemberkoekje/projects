namespace Klaverjas.core.Tafels;

/// <summary>One table in one round, by seat number: Wij 1 &amp; Wij 2 against Zij 1 &amp; Zij 2 (partners sit opposite).</summary>
public sealed record Tafel
{
    /// <summary>The first Wij player: the first on the score sheet, who writes (keeps the score) (Dad, 2026-11-25).</summary>
    public int Wij1 { get; init; }

    /// <summary>Wij 1's partner.</summary>
    public int Wij2 { get; init; }

    /// <summary>The first Zij player.</summary>
    public int Zij1 { get; init; }

    /// <summary>Zij 1's partner.</summary>
    public int Zij2 { get; init; }

    /// <summary>The four seats.</summary>
    public IReadOnlyList<int> Stoelen => [Wij1, Wij2, Zij1, Zij2];

    /// <summary>Clockwise round the table, partners opposite: Wij 1, Zij 1, Wij 2, Zij 2 (D18), as on the score sheet read row by row.</summary>
    public IReadOnlyList<int> Klokrond => [Wij1, Zij1, Wij2, Zij2];

    /// <summary>
    /// Who deals a hand: the first on the sheet writes, the second deals hand 1, then clockwise (Dad,
    /// 2026-11-25; dealing 3-2-3 clockwise, his rules).
    /// </summary>
    /// <param name="spel">The hand (1–16).</param>
    /// <returns>The dealer's seat number.</returns>
    public int Gever(int spel) => Klokrond[spel % 4];

    /// <summary>Who must play a hand: the player to the dealer's left, the third on the sheet for hand 1 (Dad, Q15 and 2026-11-25).</summary>
    /// <param name="spel">The hand (1–16).</param>
    /// <returns>The seat number.</returns>
    public int Speler(int spel) => Klokrond[(spel + 1) % 4];
}

/// <summary>One round: the tables, and the numbers that sit it out.</summary>
public sealed record Ronde
{
    /// <summary>The tables.</summary>
    public IReadOnlyList<Tafel> Tafels { get; init; } = [];

    /// <summary>The seat numbers that sit this round out (the slapers, R11/R12).</summary>
    public IReadOnlyList<int> Stil { get; init; } = [];
}

/// <summary>Who sits where in each of the three rounds, for one number of players (D13).</summary>
public sealed record Tafelschema
{
    /// <summary>The number of players.</summary>
    public int Aantal { get; init; }

    /// <summary>The three rounds.</summary>
    public IReadOnlyList<Ronde> Rondes { get; init; } = [];

    /// <summary>
    /// Whether nobody sits with the same people twice (always, with four tables or more). With fewer
    /// tables that cannot be done; then nobody gets the same partner twice.
    /// </summary>
    public bool NooitTweeKeerSamen { get; init; }

    /// <summary>How many sit out each round.</summary>
    public int StilPerRonde => Aantal % 4;

    /// <summary>How many slapers the evening has: they get the numbers 1 up to this.</summary>
    public int Slapers => 3 * StilPerRonde;

    /// <summary>The round a number sits out.</summary>
    /// <param name="nummer">The seat number.</param>
    /// <returns>1–3, or 0 when that number plays every round.</returns>
    public int RondeStil(int nummer)
    {
        for (var r = 0; r < Rondes.Count; r++)
        {
            if (Rondes[r].Stil.Contains(nummer))
            {
                return r + 1;
            }
        }

        return 0;
    }
}

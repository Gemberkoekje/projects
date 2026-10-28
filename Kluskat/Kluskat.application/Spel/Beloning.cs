namespace Kluskat.application.Spel;

/// <summary>The XP of one completion, itemised.</summary>
public sealed record XpRegel
{
    /// <summary>By size: 10, 25 or 50.</summary>
    required public int Basis { get; init; }

    /// <summary>+50% when the klus had been skipped in the last three days.</summary>
    required public int Dapper { get; init; }

    /// <summary>+5 for the first klus of the day.</summary>
    required public int EersteVanDeDag { get; init; }

    /// <summary>The XP in total.</summary>
    public int Totaal => Basis + Dapper + EersteVanDeDag;
}

/// <summary>What one "Gedaan" brought: the win screen's whole celebration.</summary>
public sealed record Beloning
{
    /// <summary>The XP this completion earned.</summary>
    public XpRegel Xp { get; init; } = new XpRegel { Basis = 0, Dapper = 0, EersteVanDeDag = 0 };

    /// <summary>The game just before.</summary>
    public Spelstand Voor { get; init; } = Spelstand.Begin;

    /// <summary>The game just after.</summary>
    public Spelstand Na { get; init; } = Spelstand.Begin;

    /// <summary>Whether this completion reached a new level.</summary>
    public bool LevelOmhoog => Na.Level > Voor.Level;

    /// <summary>Whether the cat grew into its next form.</summary>
    public bool Gegroeid => Na.Vorm != Voor.Vorm;

    /// <summary>Whether this completion made the week count for the streak.</summary>
    public bool WeekTeltNu => Voor.NogVoorDezeWeek > 0 && Na.NogVoorDezeWeek == 0;

    /// <summary>The achievements this completion earned.</summary>
    public IReadOnlyList<Trofee> NieuweTrofeeen => [.. Na.Trofeeen.Where(t => !Voor.Trofeeen.Any(v => v.Id == t.Id))];

    /// <summary>The accessories this completion unlocked.</summary>
    public IReadOnlyList<Accessoire> NieuweAccessoires => [.. Na.Accessoires.Except(Voor.Accessoires)];
}

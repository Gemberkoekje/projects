namespace Kluskat.application.Spel;

/// <summary>The game as it stands: everything the profile, the badge and the win screen show.</summary>
/// <remarks>Every number here counts what was <em>done</em>. None of them says what is left.</remarks>
public sealed record Spelstand
{
    /// <summary>The state before anything was done.</summary>
    public static Spelstand Begin { get; } = new Spelstand();

    /// <summary>Total XP ever earned.</summary>
    public int Xp { get; init; }

    /// <summary>The current level, from 1.</summary>
    public int Level { get; init; } = 1;

    /// <summary>XP earned within the current level.</summary>
    public int XpInLevel { get; init; }

    /// <summary>XP the current level takes to complete.</summary>
    public int XpVoorLevel { get; init; } = Spelregels.XpVoorLevel(1);

    /// <summary>How far through the current level, 0 to 1.</summary>
    public double Voortgang => XpVoorLevel == 0 ? 0 : (double)XpInLevel / XpVoorLevel;

    /// <summary>How grown-up the cat is.</summary>
    public KatVorm Vorm => Spelregels.Vorm(Level);

    /// <summary>How many klussen were ever ticked off.</summary>
    public int AantalKlussen { get; init; }

    /// <summary>How many klussen were done this week (Monday to Sunday).</summary>
    public int DezeWeek { get; init; }

    /// <summary>The current weekly streak, this week included once it counts.</summary>
    public int Reeks { get; init; }

    /// <summary>The longest weekly streak ever.</summary>
    public int LangsteReeks { get; init; }

    /// <summary>Freezes in hand: each one covers a week that did not count.</summary>
    public int Bevriezingen { get; init; }

    /// <summary>How many more klussen this week makes count for the streak; 0 once it does.</summary>
    public int NogVoorDezeWeek => Math.Max(0, Spelregels.KlussenPerWeek - DezeWeek);

    /// <summary>The achievements earned, in catalogue order.</summary>
    public IReadOnlyList<Trofee> Trofeeen { get; init; } = [];

    /// <summary>The accessories unlocked so far, in unlock order.</summary>
    public IReadOnlyList<Accessoire> Accessoires => [.. Spelregels.Garderobe.Where(g => g.Level <= Level).Select(g => g.Accessoire)];
}

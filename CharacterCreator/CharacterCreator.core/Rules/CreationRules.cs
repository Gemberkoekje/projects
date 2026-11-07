namespace CharacterCreator.core.Rules;

/// <summary>The rules for a new character that are not about a single skill (DESIGN.md §3.3).</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record CreationRules
{
    /// <summary>Vortex's: 15 XP, at most 3 left over.</summary>
    public static CreationRules Vortex { get; } = new () { StartXp = 15m, MaxLeftOver = 3m };

    /// <summary>The XP a new character starts with: VOID's default for a character, 15.</summary>
    required public decimal StartXp { get; init; }

    /// <summary>
    /// How much XP may be left unspent; more is lost. VOID only warns about it, so this is a warning,
    /// never a problem. Until VOID's API gives the number, the rule book holds it (3).
    /// </summary>
    required public decimal MaxLeftOver { get; init; }
}

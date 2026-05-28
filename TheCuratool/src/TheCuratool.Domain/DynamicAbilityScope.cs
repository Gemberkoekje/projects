namespace TheCuratool.Domain;

/// <summary>
/// Defines the pool of characters from which a dynamic-setup character (Alchemist / Boffin) may borrow an ability.
/// </summary>
public enum DynamicAbilityScope
{
    /// <summary>No dynamic ability scope (default for non-dynamic characters).</summary>
    Unknown = 0,

    /// <summary>The character borrows an ability from a Minion that is on the script but not in play (Alchemist).</summary>
    NotInPlayMinion = 1,

    /// <summary>The character borrows an ability from a Townsfolk or Outsider that is on the script but not in play (Boffin).</summary>
    NotInPlayTownsfolkOrOutsider = 2,
}

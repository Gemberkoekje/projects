namespace TheCuratool.Domain;

/// <summary>
/// Represents a single player's position in the draft order and their current character choice state.
/// </summary>
/// <param name="DraftOrder">The 1-based position in which this player will be offered characters.</param>
/// <param name="PlayerId">A unique identifier for this player within the session.</param>
/// <param name="Choice">The current choice state, either unchosen (with optional curated offer) or chosen.</param>
public sealed record PlayerSlot(
    int DraftOrder,
    Guid PlayerId,
    PlayerChoice Choice,
    bool IsStAssigned = false,
    string BorrowedAbilityCharacterId = "")
{
    /// <summary>Returns <see langword="true"/> if the player has made a confirmed character selection.</summary>
    public bool IsChosen => Choice is PlayerChoice.ChosenChoice || IsStAssigned;
}

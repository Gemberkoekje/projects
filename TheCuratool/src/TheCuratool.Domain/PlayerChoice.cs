namespace TheCuratool.Domain;

/// <summary>
/// Discriminated union representing the choice state of a player slot.
/// </summary>
public abstract record PlayerChoice
{
    /// <summary>
    /// The player has not yet made a final selection. May carry a curated offer of up to 3 character IDs.
    /// </summary>
    /// <param name="OfferedIds">The character IDs currently offered to this player (empty until a curated offer is created).</param>
    /// <param name="IsAtheistCommitmentConfirmed">Whether the Storyteller has locked in the Atheist constraint for this slot.</param>
    public sealed record UnchosenChoice(
        IReadOnlyList<string> OfferedIds,
        bool IsAtheistCommitmentConfirmed) : PlayerChoice
    {
        /// <summary>An empty unchosen state with no offer and no Atheist commitment.</summary>
        public static readonly UnchosenChoice Empty = new(Array.Empty<string>(), false);
    }

    /// <summary>
    /// The player has confirmed a character selection.
    /// </summary>
    /// <param name="CharacterId">The chosen character's canonical ID.</param>
    /// <param name="OfferedIds">The IDs that were offered to this player before they chose.</param>
    /// <param name="HiddenFlags">Storyteller-only flags (Drunk/Lunatic) applied at the time of choice.</param>
    public sealed record ChosenChoice(
        string CharacterId,
        IReadOnlyList<string> OfferedIds,
        HiddenFlags HiddenFlags) : PlayerChoice;
}

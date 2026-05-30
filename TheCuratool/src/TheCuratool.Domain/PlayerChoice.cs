using System.Linq;

namespace TheCuratool.Domain;

/// <summary>
/// Discriminated union representing the choice state of a player slot.
/// </summary>
public abstract record PlayerChoice
{
    /// <summary>
    /// The player has not yet made a final selection. May carry a curated offer of up to 3 options.
    /// </summary>
    /// <param name="OfferedOptions">The options currently offered to this player (empty until a curated offer is created).</param>
    public sealed record UnchosenChoice(
        IReadOnlyList<OfferOption> OfferedOptions) : PlayerChoice
    {
        public IReadOnlyList<string> OfferedIds => OfferedOptions.Select(option => option.CharacterId).ToList().AsReadOnly();

        /// <summary>An empty unchosen state with no offer.</summary>
        public static readonly UnchosenChoice Empty = new(Array.Empty<OfferOption>());

        public UnchosenChoice(IReadOnlyList<string> offeredIds)
            : this(offeredIds.Select(OfferOption.Normal).ToList().AsReadOnly())
        {
        }
    }

    /// <summary>
    /// The player has confirmed a character selection.
    /// </summary>
    /// <param name="CharacterId">The chosen character's canonical ID.</param>
    /// <param name="OfferedOptions">The options that were offered to this player before they chose.</param>
    /// <param name="HiddenFlags">Storyteller-only flags (Drunk/Lunatic) applied at the time of choice.</param>
    /// <param name="DisguiseCharacterId">The character id shown to the player for this choice; empty for normal picks.</param>
    public sealed record ChosenChoice(
        string CharacterId,
        IReadOnlyList<OfferOption> OfferedOptions,
        HiddenFlags HiddenFlags,
        string DisguiseCharacterId) : PlayerChoice
    {
        public IReadOnlyList<string> OfferedIds => OfferedOptions.Select(option => option.CharacterId).ToList().AsReadOnly();

        public ChosenChoice(string characterId, IReadOnlyList<string> offeredIds, HiddenFlags hiddenFlags)
            : this(characterId, offeredIds.Select(OfferOption.Normal).ToList().AsReadOnly(), hiddenFlags, string.Empty)
        {
        }
    }
}

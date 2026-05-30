using System;

namespace TheCuratool.Domain;

/// <summary>
/// Represents one concrete option offered during a draft pick.
/// </summary>
/// <param name="CharacterId">The real character id that will be recorded if chosen (for example "drunk" or "lunatic").</param>
/// <param name="HiddenFlags">Hidden flags to apply if this option is chosen.</param>
/// <param name="DisguiseCharacterId">The character id shown to the player. Empty for normal non-disguised offers.</param>
/// <param name="BorrowedAbilityCharacterId">The borrowed ability shown for dynamic-setup roles. Empty when the offer has no assigned borrowed ability.</param>
public sealed record OfferOption(
    string CharacterId,
    HiddenFlags HiddenFlags,
    string DisguiseCharacterId,
    string BorrowedAbilityCharacterId)
{
    public string Id => CharacterId;

    public CharacterType Type
    {
        get
        {
            if (HiddenFlags.IsDrunk)
            {
                return CharacterType.Townsfolk;
            }

            if (HiddenFlags.IsLunatic)
            {
                return CharacterType.Demon;
            }

            if (string.Equals(CharacterId, "evil", StringComparison.OrdinalIgnoreCase))
            {
                return CharacterType.Demon;
            }

            if (string.Equals(CharacterId, "drunk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(CharacterId, "lunatic", StringComparison.OrdinalIgnoreCase))
            {
                return CharacterType.Outsider;
            }

            return CharacterType.Unknown;
        }
    }

    public static OfferOption Normal(string characterId) => new(characterId, new HiddenFlags(false, false), string.Empty, string.Empty);
}

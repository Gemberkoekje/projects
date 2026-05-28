using TheCuratool.Domain;

namespace TheCuratool.Application;

public sealed record DraftStateSnapshot(
    IReadOnlyList<string> ChosenCharacterIds,
    IReadOnlyDictionary<string, HiddenFlags> HiddenFlagsByCharacterId,
    bool HasAnyMinion,
    bool HasAnyDemon,
    int PicksMade,
    int RealDemonsChosen,
    IReadOnlyList<string> BorrowedAbilityCharacterIds)
{
    public static DraftStateSnapshot FromSession(GameSession session)
    {
        var chosenIds = new List<string>();
        var hiddenFlags = new Dictionary<string, HiddenFlags>(StringComparer.OrdinalIgnoreCase);
        var borrowedAbilityIds = new List<string>();
        var hasAnyMinion = false;
        var hasAnyDemon = false;
        var realDemonsChosen = 0;

        foreach (var slot in session.Players)
        {
            if (slot.IsStAssigned)
            {
                hasAnyMinion = true;
                continue;
            }

            if (slot.Choice is not PlayerChoice.ChosenChoice chosen)
            {
                continue;
            }

            chosenIds.Add(chosen.CharacterId);
            hiddenFlags[chosen.CharacterId] = chosen.HiddenFlags;

            // Collect borrowed ability IDs from dynamic-setup slots (Alchemist / Boffin)
            if (!string.IsNullOrEmpty(slot.BorrowedAbilityCharacterId))
            {
                borrowedAbilityIds.Add(slot.BorrowedAbilityCharacterId);
            }

            if (string.Equals(chosen.CharacterId, "evil", StringComparison.OrdinalIgnoreCase))
            {
                hasAnyDemon = true;
                if (session.IsLegionGame)
                {
                    realDemonsChosen++;
                }

                continue;
            }

            var character = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, chosen.CharacterId, StringComparison.OrdinalIgnoreCase));
            if (character is null)
            {
                continue;
            }

            if (character.Type == CharacterType.Minion)
            {
                hasAnyMinion = true;
            }

            if (character.Type == CharacterType.Demon)
            {
                hasAnyDemon = true;
                if (!chosen.HiddenFlags.IsLunatic)
                {
                    realDemonsChosen++;
                }
            }
        }

        return new DraftStateSnapshot(
            chosenIds.AsReadOnly(),
            hiddenFlags,
            hasAnyMinion,
            hasAnyDemon,
            chosenIds.Count,
            realDemonsChosen,
            borrowedAbilityIds.AsReadOnly());
    }
}

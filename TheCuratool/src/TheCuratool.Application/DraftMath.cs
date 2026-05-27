using TheCuratool.Domain;

namespace TheCuratool.Application;

internal static class DraftMath
{
    public static SetupCounts ComputeCurrentCounts(GameSession session, CharacterDatabase characterDatabase)
    {
        var townsfolk = 0;
        var outsiders = 0;
        var minions = 0;
        var demons = 0;

        foreach (var slot in session.Players)
        {
            if (slot.Choice is not PlayerChoice.ChosenChoice chosen)
            {
                continue;
            }

            var definition = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, chosen.CharacterId, StringComparison.OrdinalIgnoreCase))
                ?? characterDatabase.Resolve(chosen.CharacterId);

            var effectiveType = definition.Type;
            if (chosen.HiddenFlags.IsDrunk || chosen.HiddenFlags.IsLunatic)
            {
                effectiveType = CharacterType.Outsider;
            }

            switch (effectiveType)
            {
                case CharacterType.Townsfolk:
                    townsfolk++;
                    break;
                case CharacterType.Outsider:
                    outsiders++;
                    break;
                case CharacterType.Minion:
                    minions++;
                    break;
                case CharacterType.Demon:
                    demons++;
                    break;
            }
        }

        return new SetupCounts(townsfolk, outsiders, minions, demons);
    }

    public static IReadOnlyDictionary<CharacterType, IReadOnlyList<string>> GroupChosenByUnderlyingType(GameSession session, CharacterDatabase characterDatabase)
    {
        var grouped = new Dictionary<CharacterType, List<string>>
        {
            [CharacterType.Townsfolk] = new List<string>(),
            [CharacterType.Outsider] = new List<string>(),
            [CharacterType.Minion] = new List<string>(),
            [CharacterType.Demon] = new List<string>(),
            [CharacterType.Traveller] = new List<string>(),
            [CharacterType.Fabled] = new List<string>(),
            [CharacterType.Unknown] = new List<string>(),
        };

        foreach (var slot in session.Players)
        {
            if (slot.Choice is not PlayerChoice.ChosenChoice chosen)
            {
                continue;
            }

            var definition = session.Script.Characters.FirstOrDefault(c => string.Equals(c.Id, chosen.CharacterId, StringComparison.OrdinalIgnoreCase))
                ?? characterDatabase.Resolve(chosen.CharacterId);

            grouped[definition.Type].Add(definition.DisplayName);
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.OrderBy(name => name, StringComparer.Ordinal).ToList().AsReadOnly());
    }
}

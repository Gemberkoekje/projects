namespace TheCuratool.Domain;

/// <summary>
/// An availability constraint that blocks a character once any character of a given type has already been chosen.
/// Used for Kazali (blocked after any Demon) and Summoner (blocked after any Minion).
/// </summary>
/// <param name="Type">The <see cref="CharacterType"/> whose presence in the chosen set blocks this character.</param>
public sealed record BlockedIfAnyChosenOfTypeConstraint(CharacterType Type) : IAvailabilityConstraint
{
    public bool IsAvailable(AvailabilityContext context)
    {
        return Type switch
        {
            CharacterType.Minion => !context.HasAnyMinion,
            CharacterType.Demon => !context.HasAnyDemon,
            _ => true,
        };
    }
}

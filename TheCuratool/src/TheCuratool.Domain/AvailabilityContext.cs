namespace TheCuratool.Domain;

/// <summary>
/// Provides the current draft state to <see cref="IAvailabilityConstraint"/> implementations
/// so they can decide whether a character is eligible for inclusion in an offer.
/// </summary>
/// <param name="ChosenCharacterIds">All character IDs that have been confirmed chosen so far.</param>
/// <param name="HasAnyMinion">Whether at least one Minion has been chosen.</param>
/// <param name="HasAnyDemon">Whether at least one Demon has been chosen.</param>
/// <param name="PicksMade">Total number of picks completed so far.</param>
/// <param name="IsDrunkFlagApplied">Whether the hidden Drunk flag has been applied to any chosen character.</param>
/// <param name="IsLunaticFlagApplied">Whether the hidden Lunatic flag has been applied to any chosen character.</param>
public sealed record AvailabilityContext(
    IReadOnlyList<string> ChosenCharacterIds,
    bool HasAnyMinion,
    bool HasAnyDemon,
    int PicksMade,
    bool IsDrunkFlagApplied,
    bool IsLunaticFlagApplied);

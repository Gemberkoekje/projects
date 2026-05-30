namespace TheCuratool.Domain;

/// <summary>
/// Provides contextual information passed to <see cref="ISetupRule"/> implementations during distribution calculation.
/// </summary>
/// <param name="PlayerCount">Total number of players in the game.</param>
/// <param name="InPlayCharacterIds">Character IDs that have been confirmed in play so far.</param>
/// <param name="ActiveLoricIds">IDs of Lorics currently active for this session.</param>
/// <param name="OutsiderCharacterCount">Number of Outsider characters currently available on the script.</param>
/// <param name="HasAnyDemonChosen">Whether a real Demon has already been chosen in the draft.</param>
public sealed record SetupContext(
    int PlayerCount,
    IReadOnlyList<string> InPlayCharacterIds,
    IReadOnlyList<string> ActiveLoricIds,
    int OutsiderCharacterCount,
    bool HasAnyDemonChosen);

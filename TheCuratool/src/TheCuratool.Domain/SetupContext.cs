namespace TheCuratool.Domain;

/// <summary>
/// Provides contextual information passed to <see cref="ISetupRule"/> implementations during distribution calculation.
/// </summary>
/// <param name="PlayerCount">Total number of players in the game.</param>
/// <param name="InPlayCharacterIds">Character IDs that have been confirmed in play so far.</param>
/// <param name="ActiveLoricIds">IDs of Lorics currently active for this session.</param>
public sealed record SetupContext(
    int PlayerCount,
    IReadOnlyList<string> InPlayCharacterIds,
    IReadOnlyList<string> ActiveLoricIds);

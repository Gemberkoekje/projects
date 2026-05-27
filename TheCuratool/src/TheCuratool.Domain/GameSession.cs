namespace TheCuratool.Domain;

/// <summary>
/// Represents an active or completed Curator draft session.
/// </summary>
/// <param name="Id">Unique identifier for the session.</param>
/// <param name="Script">The script used for this session.</param>
/// <param name="PlayerCount">Total number of players (5–15).</param>
/// <param name="Players">All player slots in randomised draft order.</param>
/// <param name="Status">Current lifecycle state of the session.</param>
/// <param name="ActiveLoricIds">Loric IDs that are active for this session, affecting distribution and offer generation.</param>
/// <param name="UseMarionette">When <see langword="true"/>, the pre-draft Marionette adjustment is applied (+1 Townsfolk / −1 Minion).</param>
public sealed record GameSession(
    Guid Id,
    Script Script,
    int PlayerCount,
    IReadOnlyList<PlayerSlot> Players,
    GameStatus Status,
    IReadOnlyList<string> ActiveLoricIds,
    bool UseMarionette);

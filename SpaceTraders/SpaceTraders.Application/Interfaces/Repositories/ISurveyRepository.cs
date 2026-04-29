using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Stores active surveys for later survey-aware extraction decisions.
/// </summary>
public interface ISurveyRepository
{
    /// <summary>
    /// Upserts the provided surveys for the specified ship.
    /// </summary>
    Task UpsertAsync(string shipSymbol, IReadOnlyList<SurveyModel> surveys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active surveys for a waypoint ordered by quality and expiration.
    /// </summary>
    Task<IReadOnlyList<SurveyModel>> GetActiveByWaypointAsync(string waypointSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the best currently active survey for a waypoint, optionally preferring one containing a target deposit symbol.
    /// </summary>
    Task<SurveyModel> GetBestActiveSurveyAsync(string waypointSymbol, string preferredDepositSymbol, CancellationToken cancellationToken = default);
}

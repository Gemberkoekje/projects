using SpaceTraders.Application.DTOs;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Persists a short history of completed and blocked goals per ship.
/// </summary>
/// <remarks>Phase 17e: introduced as part of the fleet dashboard implementation.</remarks>
public interface IShipGoalHistoryRepository
{
    /// <summary>Appends a new history entry for the given ship.</summary>
    Task AppendAsync(ShipGoalHistoryEntry entry, CancellationToken ct = default);

    /// <summary>Returns the most recent <paramref name="limit"/> history entries for the given ship, newest first.</summary>
    Task<IReadOnlyList<ShipGoalHistoryEntry>> GetRecentAsync(string shipSymbol, int limit, CancellationToken ct = default);
}

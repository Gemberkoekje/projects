namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Maintains an in-memory rolling history of agent credit snapshots.
/// The buffer holds up to 360 entries – one per 10-second interval – covering one hour.
/// </summary>
public interface ICreditHistoryService
{
    /// <summary>Records the current credit balance with the current UTC timestamp.</summary>
    void Record(long credits);

    /// <summary>Returns snapshots ordered oldest-first (up to 360 entries).</summary>
    IReadOnlyList<(DateTimeOffset Timestamp, long Credits)> GetHistory();
}

using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Application.Services;

/// <summary>
/// Thread-safe singleton that tracks the current active run ID in memory.
/// Ledger entries and other writes use this to tag themselves with the correct
/// <c>RunId</c> without an extra DB round-trip.
/// </summary>
public sealed class ActiveRunIdProvider : IActiveRunIdProvider
{
    private readonly object _lock = new();
    private Guid? _activeRunId;

    /// <inheritdoc />
    public Guid? ActiveRunId
    {
        get { lock (_lock) { return _activeRunId; } }
    }

    /// <inheritdoc />
    public void SetActiveRunId(Guid? runId)
    {
        lock (_lock) { _activeRunId = runId; }
    }
}

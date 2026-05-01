namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Tracks the currently active run ID in memory so that ledger entries and other
/// records can be tagged with the correct run without an extra DB round-trip on every write.
/// </summary>
public interface IActiveRunIdProvider
{
    /// <summary>Gets the ID of the currently active run, or <c>null</c> if no run is open.</summary>
    Guid? ActiveRunId { get; }

    /// <summary>Updates the tracked run ID. Called by <c>RunLifecycleService</c> whenever a run opens or closes.</summary>
    void SetActiveRunId(Guid? runId);
}

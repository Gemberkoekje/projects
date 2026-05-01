using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

/// <summary>
/// Persistence operations for <c>Run</c> and <c>ScheduledRun</c> entities.
/// </summary>
public interface IRunRepository
{
    /// <summary>Returns the currently open run (where <c>EndedAt</c> is null), or <c>null</c>.</summary>
    Task<ActiveRunInfo?> GetActiveRunAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a new run, persists it, and returns its new ID.</summary>
    Task<Guid> OpenRunAsync(
        string name,
        string strategyLabel,
        long startingCredits,
        string? settingsSnapshotJson = null,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the run by setting <c>EndedAt</c> and <c>EndingCredits</c>.</summary>
    Task CloseRunAsync(Guid runId, long endingCredits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all <c>ScheduledRun</c>s ready to be promoted: those where
    /// <c>ActivatesOnNextRestart</c> is true or <c>ActivatesAt</c> is on or before <paramref name="now"/>.
    /// </summary>
    Task<IReadOnlyList<PendingScheduledRunInfo>> GetPendingScheduledRunsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new <c>ScheduledRun</c> and returns its ID.</summary>
    Task<Guid> ScheduleRunAsync(
        string name,
        string strategyLabel,
        string? scheduledSettingsJson,
        DateTimeOffset? activatesAt,
        bool activatesOnNextRestart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the <c>ScheduledRun</c> with the given <paramref name="id"/>.
    /// Returns <c>true</c> if found and deleted, <c>false</c> if not found.
    /// </summary>
    Task<bool> DeleteScheduledRunAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a <c>ScheduledRun</c> that has been promoted to an active run.</summary>
    Task DeletePromotedScheduledRunAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Appends a <c>RunCreditHighlight</c> row for the given run.</summary>
    Task AppendCreditHighlightAsync(
        Guid runId,
        long credits,
        long deltaCredits,
        string eventKind,
        string? label = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the total number of runs ever opened for the current agent (used for run naming).</summary>
    Task<int> GetRunCountAsync(CancellationToken cancellationToken = default);
}

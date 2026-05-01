using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class RunRepository(SpaceTradersDbContext db) : IRunRepository
{
    public async Task<ActiveRunInfo?> GetActiveRunAsync(CancellationToken cancellationToken = default)
    {
        var run = await db.Runs
            .AsNoTracking()
            .Where(r => r.EndedAt == null)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return run is null
            ? null
            : new ActiveRunInfo(run.Id, run.Name, run.StrategyLabel, run.StartedAt, run.StartingCredits);
    }

    public async Task<Guid> OpenRunAsync(
        string name,
        string strategyLabel,
        long startingCredits,
        string? settingsSnapshotJson = null,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        db.Runs.Add(new Run
        {
            Id = id,
            AgentToken = db.AgentToken,
            Name = name,
            StrategyLabel = strategyLabel,
            StartedAt = TimeProvider.System.GetUtcNow(),
            SettingsSnapshotJson = settingsSnapshotJson,
            StartingCredits = startingCredits,
        });
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task CloseRunAsync(Guid runId, long endingCredits, CancellationToken cancellationToken = default)
    {
        var run = await db.Runs.FindAsync([runId], cancellationToken);
        if (run is null)
        {
            return;
        }

        run.EndedAt = TimeProvider.System.GetUtcNow();
        run.EndingCredits = endingCredits;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PendingScheduledRunInfo>> GetPendingScheduledRunsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.ScheduledRuns
            .AsNoTracking()
            .Where(r => r.ActivatesOnNextRestart || (r.ActivatesAt != null && r.ActivatesAt <= now))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new PendingScheduledRunInfo(
                r.Id, r.Name, r.StrategyLabel,
                r.ScheduledSettingsJson, r.ActivatesAt, r.ActivatesOnNextRestart))
            .ToList();
    }

    public async Task<Guid> ScheduleRunAsync(
        string name,
        string strategyLabel,
        string? scheduledSettingsJson,
        DateTimeOffset? activatesAt,
        bool activatesOnNextRestart,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        db.ScheduledRuns.Add(new ScheduledRun
        {
            Id = id,
            AgentToken = db.AgentToken,
            Name = name,
            StrategyLabel = strategyLabel,
            ScheduledSettingsJson = scheduledSettingsJson,
            ActivatesAt = activatesAt,
            ActivatesOnNextRestart = activatesOnNextRestart,
            CreatedAt = TimeProvider.System.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<bool> DeleteScheduledRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.ScheduledRuns.FindAsync([id], cancellationToken);
        if (row is null)
        {
            return false;
        }

        db.ScheduledRuns.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task DeletePromotedScheduledRunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.ScheduledRuns.FindAsync([id], cancellationToken);
        if (row is not null)
        {
            db.ScheduledRuns.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task AppendCreditHighlightAsync(
        Guid runId,
        long credits,
        long deltaCredits,
        string eventKind,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        db.RunCreditHighlights.Add(new RunCreditHighlight
        {
            AgentToken = db.AgentToken,
            RunId = runId,
            OccurredAt = TimeProvider.System.GetUtcNow(),
            Credits = credits,
            DeltaCredits = deltaCredits,
            EventKind = eventKind,
            Label = label,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetRunCountAsync(CancellationToken cancellationToken = default)
        => await db.Runs.CountAsync(cancellationToken);
}

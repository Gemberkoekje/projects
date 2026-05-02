using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Domain.Events.Ships;
using System.Collections.Concurrent;
using Wolverine;

namespace SpaceTraders.Infrastructure.Persistence;

/// <summary>
/// In-memory priority-queue scheduler for future ship events (<see cref="ShipArrivedEvent"/> and
/// <see cref="ShipCooldownExpiredEvent"/>). Implements both <see cref="IShipEventScheduler"/> and
/// <see cref="IHostedService"/> so it can reload from the database on startup and fire events
/// precisely at their scheduled time.
/// </summary>
/// <remarks>Phase 14b: introduced as part of the goal-driven architecture migration.</remarks>
[Mutable]
public sealed class ShipEventScheduler(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<ShipEventScheduler> logger) : BackgroundService, IShipEventScheduler
{
    // Each entry in the queue: the time to fire, the ship symbol, the goal id, and the event kind.
    private sealed record ScheduleEntry(DateTimeOffset TriggerAt, string ShipSymbol, Guid GoalId, string EventKind)
        : IComparable<ScheduleEntry>
    {
        public int CompareTo(ScheduleEntry? other) =>
            other is null ? 1 : TriggerAt.CompareTo(other.TriggerAt);
    }

    // A thread-safe sorted collection backed by a sorted set so we can peek the earliest entry cheaply.
    private readonly SortedSet<ScheduleEntry> _queue = new(Comparer<ScheduleEntry>.Create(
        (a, b) =>
        {
            var cmp = a.TriggerAt.CompareTo(b.TriggerAt);
            if (cmp != 0) return cmp;
            cmp = string.Compare(a.ShipSymbol, b.ShipSymbol, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return a.GoalId.CompareTo(b.GoalId);
        }));

    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly object _lock = new();

    // ───────────────────────────────────────────────────────────────
    // Disposal
    // ───────────────────────────────────────────────────────────────

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }

    // ───────────────────────────────────────────────────────────────
    // IShipEventScheduler implementation
    // ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ScheduleArrivalAsync(string shipSymbol, Guid goalId, DateTimeOffset arrivalTime, CancellationToken ct)
    {
        await UpsertDbRowAsync(shipSymbol, goalId, "Arrival", arrivalTime, ct);
        EnqueueInMemory(new ScheduleEntry(arrivalTime, shipSymbol, goalId, "Arrival"));
        logger.LogDebug("ShipEventScheduler: queued Arrival for {Ship} / goal {Goal} at {Time}.", shipSymbol, goalId, arrivalTime);
    }

    /// <inheritdoc/>
    public async Task ScheduleCooldownExpiryAsync(string shipSymbol, Guid goalId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await UpsertDbRowAsync(shipSymbol, goalId, "CooldownExpiry", expiresAt, ct);
        EnqueueInMemory(new ScheduleEntry(expiresAt, shipSymbol, goalId, "CooldownExpiry"));
        logger.LogDebug("ShipEventScheduler: queued CooldownExpiry for {Ship} / goal {Goal} at {Time}.", shipSymbol, goalId, expiresAt);
    }

    /// <inheritdoc/>
    public async Task CancelScheduledAsync(string shipSymbol, Guid goalId, CancellationToken ct)
    {
        await DeleteDbRowAsync(shipSymbol, goalId, ct);
        RemoveFromMemory(shipSymbol, goalId);
        logger.LogDebug("ShipEventScheduler: cancelled scheduled event for {Ship} / goal {Goal}.", shipSymbol, goalId);
    }

    // ───────────────────────────────────────────────────────────────
    // BackgroundService execution loop
    // ───────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadFromDatabaseAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeNextDelay();

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    // Wait until the next scheduled trigger time, or until signalled early by a
                    // newly scheduled event that is sooner than anything in the queue.
                    await _signal.WaitAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            await FireDueEventsAsync(stoppingToken);
        }
    }

    // ───────────────────────────────────────────────────────────────
    // Internal helpers
    // ───────────────────────────────────────────────────────────────

    private async Task LoadFromDatabaseAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

            var rows = await db.ScheduledShipEvents
                .AsNoTracking()
                .ToListAsync(ct);

            lock (_lock)
            {
                foreach (var row in rows)
                {
                    _queue.Add(new ScheduleEntry(row.TriggerAt, row.ShipSymbol, row.GoalId, row.EventKind));
                }
            }

            logger.LogInformation("ShipEventScheduler: loaded {Count} pending event(s) from database.", rows.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShipEventScheduler: failed to load pending events from database on startup.");
        }
    }

    private async Task FireDueEventsAsync(CancellationToken ct)
    {
        var now = TimeProvider.System.GetUtcNow();
        List<ScheduleEntry> due;

        lock (_lock)
        {
            due = _queue.TakeWhile(e => e.TriggerAt <= now).ToList();
            foreach (var entry in due)
            {
                _queue.Remove(entry);
            }
        }

        foreach (var entry in due)
        {
            await FireAndDeleteAsync(entry, ct);
        }
    }

    private async Task FireAndDeleteAsync(ScheduleEntry entry, CancellationToken ct)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            // Delete the DB row first so even if event publishing fails we don't double-fire on restart.
            await db.ScheduledShipEvents
                .Where(e => e.ShipSymbol == entry.ShipSymbol && e.GoalId == entry.GoalId)
                .ExecuteDeleteAsync(ct);

            var now = TimeProvider.System.GetUtcNow();

            if (entry.EventKind == "Arrival")
            {
                await bus.PublishAsync(new ShipArrivedEvent(entry.ShipSymbol, entry.GoalId, now));
                logger.LogInformation("ShipEventScheduler: fired ShipArrivedEvent for {Ship} / goal {Goal}.", entry.ShipSymbol, entry.GoalId);
            }
            else
            {
                await bus.PublishAsync(new ShipCooldownExpiredEvent(entry.ShipSymbol, entry.GoalId, now));
                logger.LogInformation("ShipEventScheduler: fired ShipCooldownExpiredEvent for {Ship} / goal {Goal}.", entry.ShipSymbol, entry.GoalId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShipEventScheduler: error firing event for {Ship} / goal {Goal}.", entry.ShipSymbol, entry.GoalId);
        }
    }

    private TimeSpan ComputeNextDelay()
    {
        ScheduleEntry? earliest;
        lock (_lock)
        {
            earliest = _queue.Min;
        }

        if (earliest is null)
        {
            // Nothing scheduled; wait up to 30 seconds before re-checking.
            return TimeSpan.FromSeconds(30);
        }

        var delay = earliest.TriggerAt - TimeProvider.System.GetUtcNow();
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    private void EnqueueInMemory(ScheduleEntry entry)
    {
        lock (_lock)
        {
            // Remove any existing entry for this (ship, goal) pair and replace it.
            _queue.RemoveWhere(e => e.ShipSymbol == entry.ShipSymbol && e.GoalId == entry.GoalId);
            _queue.Add(entry);
        }

        // Signal the background loop so it wakes up if the new entry fires sooner than the current head.
        _signal.Release();
    }

    private void RemoveFromMemory(string shipSymbol, Guid goalId)
    {
        lock (_lock)
        {
            _queue.RemoveWhere(e => e.ShipSymbol == shipSymbol && e.GoalId == goalId);
        }
    }

    private async Task UpsertDbRowAsync(string shipSymbol, Guid goalId, string eventKind, DateTimeOffset triggerAt, CancellationToken ct)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

            var existing = await db.ScheduledShipEvents.FindAsync([shipSymbol, goalId], ct);
            if (existing is null)
            {
                db.ScheduledShipEvents.Add(new Entities.ScheduledShipEvent
                {
                    ShipSymbol = shipSymbol,
                    GoalId = goalId,
                    EventKind = eventKind,
                    TriggerAt = triggerAt,
                });
            }
            else
            {
                existing.TriggerAt = triggerAt;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShipEventScheduler: failed to persist scheduled event for {Ship} / goal {Goal}.", shipSymbol, goalId);
        }
    }

    private async Task DeleteDbRowAsync(string shipSymbol, Guid goalId, CancellationToken ct)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

            await db.ScheduledShipEvents
                .Where(e => e.ShipSymbol == shipSymbol && e.GoalId == goalId)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ShipEventScheduler: failed to delete scheduled event for {Ship} / goal {Goal}.", shipSymbol, goalId);
        }
    }
}

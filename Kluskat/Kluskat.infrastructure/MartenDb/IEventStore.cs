namespace Kluskat.infrastructure.MartenDb;

/// <summary>The Marten-free view on an event store, so the repository can be tested in memory.</summary>
/// <typeparam name="TId">The stream identity.</typeparam>
public interface IEventStore<TId>
{
    /// <summary>Appends events, failing when the stream is no longer at <paramref name="expectedVersion"/>.</summary>
    /// <param name="aggregateId">The stream.</param>
    /// <param name="expectedVersion">The version the caller loaded.</param>
    /// <param name="events">The new events.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <returns>OK, or a message on a concurrency clash.</returns>
    Task<Result> SaveEventsAsync(TId aggregateId, long expectedVersion, IEnumerable<object> events, CancellationToken cancellationToken = default);

    /// <summary>Loads a whole stream.</summary>
    /// <param name="aggregateId">The stream.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The events, oldest first; empty for an unknown stream.</returns>
    Task<IReadOnlyList<object>> LoadEventsAsync(TId aggregateId, CancellationToken cancellationToken = default);
}

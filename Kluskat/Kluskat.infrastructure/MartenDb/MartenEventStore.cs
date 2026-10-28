using JasperFx.Events;
using Marten;

namespace Kluskat.infrastructure.MartenDb;

/// <summary>Marten's event store behind <see cref="IEventStore{TId}"/>.</summary>
public sealed class MartenEventStore(IDocumentStore store) : IEventStore<Uuid>
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public Task<Result> SaveEventsAsync(Uuid aggregateId, long expectedVersion, IEnumerable<object> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        return AppendAsync(aggregateId, expectedVersion, [.. events], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<object>> LoadEventsAsync(Uuid aggregateId, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();

        var stream = await session.Events.FetchStreamAsync((Guid)aggregateId, token: cancellationToken);
        return [.. stream.Select(e => e.Data)];
    }

    private async Task<Result> AppendAsync(Uuid aggregateId, long expectedVersion, object[] buffered, CancellationToken cancellationToken)
    {
        if (buffered.Length == 0)
        {
            return Result.OK;
        }

        try
        {
            using var session = store.LightweightSession();

            var stream = session.Events.Append((Guid)aggregateId, buffered);
            stream.ExpectedVersionOnServer = expectedVersion;

            await session.SaveChangesAsync(cancellationToken);
            return Result.OK;
        }
        catch (EventStreamUnexpectedMaxEventIdException)
        {
            // One user, but two devices: a double tap on the phone and the laptop can still race.
            return Result.WithMessages(ValidationMessage.Error("Deze klus is net al bijgewerkt. Ververs de pagina en probeer het opnieuw."));
        }
    }
}

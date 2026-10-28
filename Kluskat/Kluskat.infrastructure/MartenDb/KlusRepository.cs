using Qowaiv.DomainModel;

namespace Kluskat.infrastructure.MartenDb;

/// <summary>Replays and appends <see cref="Klus"/> streams; knows nothing about read models.</summary>
public sealed class KlusRepository(IEventStore<Uuid> eventStore)
{
    private readonly IEventStore<Uuid> eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));

    /// <summary>Replays a klus.</summary>
    /// <param name="id">The klus.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The klus, or a message when it does not exist.</returns>
    public async Task<Result<Klus>> LadenAsync(KlusId id, CancellationToken cancellationToken = default)
    {
        if (id == KlusId.Empty)
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Geen klus opgegeven."));
        }

        var events = await eventStore.LoadEventsAsync((Uuid)id, cancellationToken);
        if (events.Count == 0)
        {
            return Result.WithMessages<Klus>(ValidationMessage.Error("Deze klus bestaat niet (meer)."));
        }

        var buffer = EventBuffer.FromStorage(id, events, e => e);
        return Aggregate.FromStorage<Klus, KlusId>(buffer);
    }

    /// <summary>Appends the buffered events of a klus.</summary>
    /// <param name="klus">The klus.</param>
    /// <param name="cancellationToken">Cancels the append.</param>
    /// <returns>OK, or a message on a concurrency clash.</returns>
    public Task<Result> OpslaanAsync(Klus klus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return AppendAsync(klus, cancellationToken);
    }

    private async Task<Result> AppendAsync(Klus klus, CancellationToken cancellationToken)
    {
        var result = await eventStore.SaveEventsAsync(
            (Uuid)klus.Id,
            klus.Buffer.CommittedVersion,
            klus.Buffer.Uncommitted,
            cancellationToken);

        if (result.IsValid)
        {
            klus.MarkAllAsCommitted();
        }

        return result;
    }
}

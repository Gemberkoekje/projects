using Kluskat.infrastructure.MartenDb;

namespace Kluskat.test.TestHelpers;

/// <summary>An event store in a dictionary, with the same concurrency rule as Marten's.</summary>
internal sealed class InMemoryEventStore : IEventStore<Uuid>
{
    private readonly Dictionary<Uuid, List<object>> streams = [];

    public Task<Result> SaveEventsAsync(Uuid aggregateId, long expectedVersion, IEnumerable<object> events, CancellationToken cancellationToken = default)
    {
        if (!streams.TryGetValue(aggregateId, out var stream))
        {
            stream = [];
            streams[aggregateId] = stream;
        }

        if (stream.Count != expectedVersion)
        {
            return Task.FromResult(Result.WithMessages(ValidationMessage.Error("Conflict.")));
        }

        stream.AddRange(events);
        return Task.FromResult(Result.OK);
    }

    public Task<IReadOnlyList<object>> LoadEventsAsync(Uuid aggregateId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<object>>(streams.TryGetValue(aggregateId, out var stream) ? [.. stream] : []);

    public IReadOnlyList<object> Stream(KlusId id) => streams.TryGetValue((Uuid)id, out var stream) ? stream : [];
}

/// <summary>The repository over the in-memory event store, projecting into the in-memory overzicht store.</summary>
internal sealed class InMemoryKlusRepository(InMemoryEventStore events, InMemoryKlusOverzichtStore overzichten) : IKlusRepository
{
    private readonly KlusRepository repository = new KlusRepository(events);

    public Task<Result<Klus>> LadenAsync(KlusId id, CancellationToken cancellationToken = default)
        => repository.LadenAsync(id, cancellationToken);

    public async Task<Result> OpslaanAsync(Klus klus, CancellationToken cancellationToken = default)
    {
        var result = await repository.OpslaanAsync(klus, cancellationToken);
        if (result.IsValid)
        {
            overzichten.Bewaar(KlusOverzicht.Van(klus));
        }

        return result;
    }
}

internal sealed class InMemoryKlusOverzichtStore : IKlusOverzichtStore
{
    private readonly Dictionary<Guid, KlusOverzicht> documenten = [];

    public IReadOnlyCollection<KlusOverzicht> Alle => documenten.Values;

    public void Bewaar(KlusOverzicht overzicht) => documenten[overzicht.Id] = overzicht;

    public Task<IReadOnlyList<KlusOverzicht>> ActieveAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<KlusOverzicht>>([.. documenten.Values.Where(k => !k.Geschrapt)]);

    public Task<Result<KlusOverzicht>> LadenAsync(KlusId id, CancellationToken cancellationToken = default)
        => Task.FromResult(documenten.TryGetValue((Guid)(Uuid)id, out var document)
            ? Result.For(document)
            : Result.WithMessages<KlusOverzicht>(ValidationMessage.Error("Onbekend.")));

    public Task<bool> HeeftKlussenAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(documenten.Count > 0);
}

internal sealed class InMemorySessieStore : ISessieStore
{
    public Sessie Sessie { get; private set; } = new Sessie();

    public Task<Sessie> LadenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Sessie);

    public Task OpslaanAsync(Sessie sessie, CancellationToken cancellationToken = default)
    {
        Sessie = sessie;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryVoltooiingStore : IVoltooiingStore
{
    private readonly List<Voltooiing> log = [];

    public IReadOnlyList<Voltooiing> Log => log;

    public Task ToevoegenAsync(Voltooiing voltooiing, CancellationToken cancellationToken = default)
    {
        log.Add(voltooiing);
        return Task.CompletedTask;
    }

    public Task LaatsteVerwijderenAsync(KlusId id, CancellationToken cancellationToken = default)
    {
        var laatste = log.Where(v => v.KlusId == (Guid)(Uuid)id).OrderByDescending(v => v.Op).FirstOrDefault();
        if (laatste is not null)
        {
            log.Remove(laatste);
        }

        return Task.CompletedTask;
    }

    public Task<int> AantalSindsAsync(DateTime sinds, CancellationToken cancellationToken = default)
        => Task.FromResult(log.Count(v => v.Op >= sinds));
}

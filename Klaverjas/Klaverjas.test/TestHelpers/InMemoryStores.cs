namespace Klaverjas.test.TestHelpers;

internal sealed class InMemorySpelerStore : ISpelerStore
{
    private readonly Dictionary<Guid, Speler> spelers = [];

    public InMemorySpelerStore(params Speler[] begin)
    {
        foreach (var speler in begin)
        {
            spelers[speler.Id] = speler;
        }
    }

    public IReadOnlyCollection<Speler> Alle => spelers.Values;

    public Task<IReadOnlyList<Speler>> AllesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Speler>>([.. spelers.Values]);

    public Task<Result<Speler>> LadenAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(spelers.TryGetValue(id, out var speler)
            ? Result.For(speler)
            : Result.WithMessages<Speler>(ValidationMessage.Error("Die speler bestaat niet (meer).")));

    public Task OpslaanAsync(Speler speler, CancellationToken cancellationToken = default)
    {
        spelers[speler.Id] = speler;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAvondStore : IAvondStore
{
    private readonly Dictionary<DateOnly, Avond> avonden = [];

    public InMemoryAvondStore(params Avond[] begin)
    {
        foreach (var avond in begin)
        {
            avonden[avond.Datum] = avond;
        }
    }

    public IReadOnlyCollection<Avond> Alle => avonden.Values;

    public Task<IReadOnlyList<Avond>> AllesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Avond>>([.. avonden.Values]);

    public Task<Result<Avond>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default)
        => Task.FromResult(avonden.TryGetValue(datum, out var avond)
            ? Result.For(avond)
            : Result.WithMessages<Avond>(ValidationMessage.Error("Die avond is nog niet ingevuld.")));

    public Task OpslaanAsync(Avond avond, CancellationToken cancellationToken = default)
    {
        avonden[avond.Datum] = avond;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryInstellingenStore(Instellingen begin) : IInstellingenStore
{
    public InMemoryInstellingenStore()
        : this(new Instellingen())
    {
    }

    public Instellingen Huidig { get; private set; } = begin;

    public Task<Instellingen> LadenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Huidig);

    public Task OpslaanAsync(Instellingen instellingen, CancellationToken cancellationToken = default)
    {
        Huidig = instellingen;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryVoorbereidingStore : IVoorbereidingStore
{
    private readonly Dictionary<DateOnly, Voorbereiding> voorbereidingen = [];

    public InMemoryVoorbereidingStore(params Voorbereiding[] begin)
    {
        foreach (var voorbereiding in begin)
        {
            voorbereidingen[voorbereiding.Datum] = voorbereiding;
        }
    }

    public IReadOnlyCollection<Voorbereiding> Alle => voorbereidingen.Values;

    public Task<Result<Voorbereiding>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default)
        => Task.FromResult(voorbereidingen.TryGetValue(datum, out var voorbereiding)
            ? Result.For(voorbereiding)
            : Result.WithMessages<Voorbereiding>(ValidationMessage.Error("Die avond is nog niet voorbereid.")));

    public Task OpslaanAsync(Voorbereiding voorbereiding, CancellationToken cancellationToken = default)
    {
        voorbereidingen[voorbereiding.Datum] = voorbereiding;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryOnlineStandStore : IOnlineStandStore
{
    public OnlineStand Huidig { get; private set; } = new OnlineStand();

    public Task<OnlineStand> LadenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Huidig);

    public Task OpslaanAsync(OnlineStand online, CancellationToken cancellationToken = default)
    {
        Huidig = online;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryVerzendingStore : IVerzendingStore
{
    private readonly List<Verzending> verzendingen = [];

    public IReadOnlyList<Verzending> Alle => verzendingen;

    public Task<IReadOnlyList<Verzending>> AllesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Verzending>>([.. verzendingen]);

    public Task OpslaanAsync(Verzending verzending, CancellationToken cancellationToken = default)
    {
        verzendingen.Add(verzending);
        return Task.CompletedTask;
    }
}

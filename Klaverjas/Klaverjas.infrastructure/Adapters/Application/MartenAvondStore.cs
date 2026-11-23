using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>
/// An evening as it was before it was saved again (D8). Nothing reads these yet; they are there so a
/// correction can always be traced or undone by hand.
/// </summary>
public sealed record AvondVersie
{
    /// <summary>The document identity.</summary>
    public Guid Id { get; init; }

    /// <summary>The evening.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>When this version was replaced (UTC).</summary>
    public DateTime VervangenOp { get; init; }

    /// <summary>The results as they were.</summary>
    public IReadOnlyList<Uitslag> Uitslagen { get; init; } = [];
}

/// <summary>Keeps the evenings as <see cref="Avond"/> documents, and every replaced one as an <see cref="AvondVersie"/>.</summary>
public sealed class MartenAvondStore(IDocumentStore store) : IAvondStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Avond>> AllesAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Avond>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<Avond>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var avond = await session.LoadAsync<Avond>(Avond.IdVan(datum), cancellationToken);
        return avond is null
            ? Result.WithMessages<Avond>(ValidationMessage.Error("Die avond is nog niet ingevuld."))
            : Result.For(avond);
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Avond avond, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(avond);

        return StoreAsync(avond, cancellationToken);
    }

    private async Task StoreAsync(Avond avond, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();

        var vorige = await session.LoadAsync<Avond>(avond.Id, cancellationToken);
        if (vorige is not null)
        {
            session.Store(new AvondVersie { Id = Guid.NewGuid(), Datum = vorige.Datum, VervangenOp = Clock.UtcNow(), Uitslagen = vorige.Uitslagen });
        }

        session.Store(avond);
        await session.SaveChangesAsync(cancellationToken);
    }
}

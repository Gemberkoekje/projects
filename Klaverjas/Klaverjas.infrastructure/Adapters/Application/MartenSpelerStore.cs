using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps the players as <see cref="Speler"/> documents.</summary>
public sealed class MartenSpelerStore(IDocumentStore store) : ISpelerStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Speler>> AllesAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Speler>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<Speler>> LadenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var speler = await session.LoadAsync<Speler>(id, cancellationToken);
        return speler is null
            ? Result.WithMessages<Speler>(ValidationMessage.Error("Die speler bestaat niet (meer)."))
            : Result.For(speler);
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Speler speler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(speler);

        return StoreAsync(speler, cancellationToken);
    }

    /// <inheritdoc />
    public async Task WeghalenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var session = store.LightweightSession();
        session.Delete<Speler>(id);
        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task StoreAsync(Speler speler, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(speler);
        await session.SaveChangesAsync(cancellationToken);
    }
}

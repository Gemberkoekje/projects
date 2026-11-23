using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps each evening's preparation as a <see cref="Voorbereiding"/> document.</summary>
public sealed class MartenVoorbereidingStore(IDocumentStore store) : IVoorbereidingStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<Voorbereiding>> LadenAsync(DateOnly datum, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var voorbereiding = await session.LoadAsync<Voorbereiding>(Voorbereiding.IdVan(datum), cancellationToken);
        return voorbereiding is null
            ? Result.WithMessages<Voorbereiding>(ValidationMessage.Error("Die avond is nog niet voorbereid."))
            : Result.For(voorbereiding);
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Voorbereiding voorbereiding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(voorbereiding);

        return StoreAsync(voorbereiding, cancellationToken);
    }

    private async Task StoreAsync(Voorbereiding voorbereiding, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(voorbereiding);
        await session.SaveChangesAsync(cancellationToken);
    }
}

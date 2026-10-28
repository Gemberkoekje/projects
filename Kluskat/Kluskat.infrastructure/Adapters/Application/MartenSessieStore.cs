using Marten;

namespace Kluskat.infrastructure.Adapters.Application;

/// <summary>Keeps the one <see cref="Sessie"/> document.</summary>
public sealed class MartenSessieStore(IDocumentStore store) : ISessieStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Sessie> LadenAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.LoadAsync<Sessie>(Sessie.HuidigeId, cancellationToken) ?? new Sessie();
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Sessie sessie, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessie);

        return StoreAsync(sessie, cancellationToken);
    }

    private async Task StoreAsync(Sessie sessie, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(sessie);
        await session.SaveChangesAsync(cancellationToken);
    }
}

using Marten;

namespace Kluskat.infrastructure.Adapters.Application;

/// <summary>Keeps the one <see cref="Profiel"/> document.</summary>
public sealed class MartenProfielStore(IDocumentStore store) : IProfielStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Profiel> LadenAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.LoadAsync<Profiel>(Profiel.HuidigeId, cancellationToken) ?? new Profiel();
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Profiel profiel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiel);

        return StoreAsync(profiel, cancellationToken);
    }

    private async Task StoreAsync(Profiel profiel, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(profiel);
        await session.SaveChangesAsync(cancellationToken);
    }
}

using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps the one <see cref="Instellingen"/> document.</summary>
public sealed class MartenInstellingenStore(IDocumentStore store) : IInstellingenStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Instellingen> LadenAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.LoadAsync<Instellingen>(Instellingen.HuidigeId, cancellationToken) ?? new Instellingen();
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Instellingen instellingen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instellingen);

        return StoreAsync(instellingen, cancellationToken);
    }

    private async Task StoreAsync(Instellingen instellingen, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(instellingen with { Id = Instellingen.HuidigeId });
        await session.SaveChangesAsync(cancellationToken);
    }
}

using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps the one <see cref="OnlineStand"/> document: what the players' page shows.</summary>
public sealed class MartenOnlineStandStore(IDocumentStore store) : IOnlineStandStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<OnlineStand> LadenAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.LoadAsync<OnlineStand>(OnlineStand.HuidigeId, cancellationToken) ?? new OnlineStand();
    }

    /// <inheritdoc />
    public Task OpslaanAsync(OnlineStand online, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(online);

        return StoreAsync(online, cancellationToken);
    }

    private async Task StoreAsync(OnlineStand online, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(online with { Id = OnlineStand.HuidigeId });
        await session.SaveChangesAsync(cancellationToken);
    }
}

using Marten;

namespace Klaverjas.infrastructure.Adapters.Application;

/// <summary>Keeps every <see cref="Verzending"/>: the send log.</summary>
public sealed class MartenVerzendingStore(IDocumentStore store) : IVerzendingStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Verzending>> AllesAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var alle = await session.Query<Verzending>().ToListAsync(cancellationToken);
        return [.. alle.OrderBy(v => v.Verstuurd)];
    }

    /// <inheritdoc />
    public Task OpslaanAsync(Verzending verzending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verzending);

        return StoreAsync(verzending, cancellationToken);
    }

    private async Task StoreAsync(Verzending verzending, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(verzending);
        await session.SaveChangesAsync(cancellationToken);
    }
}

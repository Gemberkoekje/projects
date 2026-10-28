using Marten;

namespace Kluskat.infrastructure.Adapters.Application;

/// <summary>Keeps the completion log as <see cref="Voltooiing"/> documents.</summary>
public sealed class MartenVoltooiingStore(IDocumentStore store) : IVoltooiingStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public Task ToevoegenAsync(Voltooiing voltooiing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(voltooiing);

        return StoreAsync(voltooiing, cancellationToken);
    }

    private async Task StoreAsync(Voltooiing voltooiing, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(voltooiing);
        await session.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task LaatsteVerwijderenAsync(KlusId id, CancellationToken cancellationToken = default)
    {
        var klusId = (Guid)(Uuid)id;

        using var session = store.LightweightSession();
        var laatste = await session.Query<Voltooiing>()
            .Where(v => v.KlusId == klusId)
            .OrderByDescending(v => v.Op)
            .FirstOrDefaultAsync(cancellationToken);

        if (laatste is not null)
        {
            session.Delete(laatste);
            await session.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Voltooiing>> AlleAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Voltooiing>().ToListAsync(cancellationToken);
    }
}

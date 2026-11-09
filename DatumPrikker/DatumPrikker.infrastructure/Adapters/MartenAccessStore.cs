using DatumPrikker.core.Enums;
using Marten;

namespace DatumPrikker.infrastructure.Adapters;

/// <summary>Keeps <see cref="Access"/> documents in Marten, one per account id.</summary>
public sealed class MartenAccessStore(IDocumentStore store) : IAccessStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<Access>> LoadAsync(string accountId, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var access = await session.LoadAsync<Access>(accountId, cancellationToken);
        return access is null
            ? Result.WithMessages<Access>(ValidationMessage.Error("Er is nog geen aanvraag.", nameof(accountId)))
            : access;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Access>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Access>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountRequestedAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Access>().CountAsync(x => x.State == AccessState.Requested, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> SaveAsync(Access access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(access);

        return StoreAsync(access, cancellationToken);
    }

    private async Task<Result> StoreAsync(Access access, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(access);
        await session.SaveChangesAsync(cancellationToken);
        return Result.OK;
    }
}

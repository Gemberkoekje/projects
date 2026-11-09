using Marten;

namespace DatumPrikker.infrastructure.Adapters;

/// <summary>Keeps <see cref="Poll"/> documents in Marten, found by their unique share token.</summary>
public sealed class MartenPollStore(IDocumentStore store) : IPollStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<Poll>> LoadAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var poll = await session.Query<Poll>().FirstOrDefaultAsync(x => x.ShareToken == shareToken, cancellationToken);
        return poll is null
            ? Result.WithMessages<Poll>(ValidationMessage.Error(IPollStore.NotFound, nameof(shareToken)))
            : poll;
    }

    /// <inheritdoc />
    public async Task<bool> IsShareTokenTakenAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Poll>().AnyAsync(x => x.ShareToken == shareToken, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> SaveAsync(Poll poll, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poll);

        return StoreAsync(poll, cancellationToken);
    }

    private async Task<Result> StoreAsync(Poll poll, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(poll);
        await session.SaveChangesAsync(cancellationToken);
        return Result.OK;
    }
}

using Marten;

namespace DatumPrikker.infrastructure.Adapters;

/// <summary>Keeps <see cref="PreApproval"/> documents in Marten, one per normalised email address.</summary>
public sealed class MartenPreApprovalStore(IDocumentStore store) : IPreApprovalStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<bool> IsPreApprovedAsync(string email, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.LoadAsync<PreApproval>(email, cancellationToken) is not null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreApproval>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<PreApproval>().ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> SaveAsync(PreApproval preApproval, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preApproval);

        return StoreAsync(preApproval, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string email, CancellationToken cancellationToken = default)
    {
        using var session = store.LightweightSession();
        session.Delete<PreApproval>(email);
        await session.SaveChangesAsync(cancellationToken);
        return Result.OK;
    }

    private async Task<Result> StoreAsync(PreApproval preApproval, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(preApproval);
        await session.SaveChangesAsync(cancellationToken);
        return Result.OK;
    }
}

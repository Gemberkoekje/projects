namespace DatumPrikker.application.Queries;

/// <summary>Sorts every access into waiting, approved and refused, plus the pre-approvals; admins only.</summary>
public sealed class ListAccessHandler(IAccessStore access, IPreApprovalStore preApprovals, Admins admins)
{
    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly IPreApprovalStore preApprovals = preApprovals ?? throw new ArgumentNullException(nameof(preApprovals));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Who asks.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The overview, or <see cref="Admins.OnlyMessage"/>.</returns>
    public Task<Result<AccessOverview>> HandleAsync(ListAccess query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return admins.Includes(query.By)
            ? ListAsync(cancellationToken)
            : Task.FromResult(Result.WithMessages<AccessOverview>(admins.Require(query.By).Messages));
    }

    private async Task<Result<AccessOverview>> ListAsync(CancellationToken cancellationToken)
    {
        var all = await access.ListAsync(cancellationToken);
        var addresses = await preApprovals.ListAsync(cancellationToken);

        return new AccessOverview
        {
            Requested = [.. all.Where(x => x.State == AccessState.Requested).OrderBy(x => x.RequestedOn)],
            Approved = [.. all.Where(x => x.State == AccessState.Approved).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)],
            Refused = [.. all.Where(x => x.State is AccessState.Declined or AccessState.Revoked).OrderByDescending(x => x.DecidedOn)],
            PreApprovals = [.. addresses.OrderBy(x => x.Id, StringComparer.Ordinal)],
        };
    }
}

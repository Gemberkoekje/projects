namespace DatumPrikker.application.Queries;

/// <summary>Counts waiting requests for admins; for anybody else it answers 0 without asking the store.</summary>
public sealed class CountPendingRequestsHandler(IAccessStore access, Admins admins)
{
    private readonly IAccessStore access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly Admins admins = admins ?? throw new ArgumentNullException(nameof(admins));

    /// <summary>Handles the query.</summary>
    /// <param name="query">Who is looking.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of waiting requests; 0 for non-admins.</returns>
    public Task<int> HandleAsync(CountPendingRequests query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return admins.Includes(query.By)
            ? access.CountRequestedAsync(cancellationToken)
            : Task.FromResult(0);
    }
}

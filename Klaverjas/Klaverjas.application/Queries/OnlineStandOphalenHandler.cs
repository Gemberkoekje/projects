namespace Klaverjas.application.Queries;

/// <summary>Loads what is online. Never computes: the players see only what Dad put online (D2).</summary>
public sealed class OnlineStandOphalenHandler(IOnlineStandStore online)
{
    private readonly IOnlineStandStore online = online ?? throw new ArgumentNullException(nameof(online));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The standings online; an empty one when nothing is online yet.</returns>
    public Task<Result<OnlineStand>> HandleAsync(OnlineStandOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<OnlineStand>> OphalenAsync(CancellationToken cancellationToken)
        => Result.For(await online.LadenAsync(cancellationToken));
}

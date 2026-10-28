namespace Kluskat.application.Queries;

/// <summary>Loads one klus.</summary>
public sealed class KlusOphalenHandler(IKlusOverzichtStore overzichten)
{
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The klus, or a message when it does not exist.</returns>
    public Task<Result<KlusOverzicht>> HandleAsync(KlusOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return overzichten.LadenAsync(query.KlusId, cancellationToken);
    }
}

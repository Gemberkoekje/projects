namespace Klaverjas.application.Queries;

/// <summary>Loads the settings.</summary>
public sealed class InstellingenOphalenHandler(IInstellingenStore instellingen)
{
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The settings.</returns>
    public Task<Result<Instellingen>> HandleAsync(InstellingenOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<Instellingen>> OphalenAsync(CancellationToken cancellationToken)
        => Result.For(await instellingen.LadenAsync(cancellationToken));
}

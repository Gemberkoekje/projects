namespace Klaverjas.application.Queries;

/// <summary>Loads a planned import.</summary>
public sealed class ImportVoorstelOphalenHandler(IImportVoorstelStore voorstellen)
{
    private readonly IImportVoorstelStore voorstellen = voorstellen ?? throw new ArgumentNullException(nameof(voorstellen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The plan.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The plan, or an error when it is gone.</returns>
    public Task<Result<ImportVoorstel>> HandleAsync(ImportVoorstelOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return voorstellen.LadenAsync(query.Id, cancellationToken);
    }
}

namespace Klaverjas.application.Commands;

/// <summary>Forgets a planned import.</summary>
public sealed class ImportAnnulerenHandler(IImportVoorstelStore voorstellen)
{
    private readonly IImportVoorstelStore voorstellen = voorstellen ?? throw new ArgumentNullException(nameof(voorstellen));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The plan.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK; a plan that is already gone is not an error.</returns>
    public Task<Result> HandleAsync(ImportAnnuleren command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AnnulerenAsync(command, cancellationToken);
    }

    private async Task<Result> AnnulerenAsync(ImportAnnuleren command, CancellationToken cancellationToken)
    {
        await voorstellen.VerwijderenAsync(command.Id, cancellationToken);
        return Result.OK;
    }
}

namespace Klaverjas.application.Commands;

/// <summary>Changes a coupling and recomputes the differences.</summary>
public sealed class ImportKoppelingWijzigenHandler(IImportVoorstelStore voorstellen, IAvondStore avonden, IInstellingenStore instellingen)
{
    private readonly IImportVoorstelStore voorstellen = voorstellen ?? throw new ArgumentNullException(nameof(voorstellen));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The plan and the new coupling.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or an error when the plan is gone or the names are unknown.</returns>
    public Task<Result> HandleAsync(ImportKoppelingWijzigen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return WijzigenAsync(command, cancellationToken);
    }

    private async Task<Result> WijzigenAsync(ImportKoppelingWijzigen command, CancellationToken cancellationToken)
    {
        var geladen = await voorstellen.LadenAsync(command.Id, cancellationToken);
        if (!geladen.IsValid)
        {
            return Result.WithMessages(geladen.Messages);
        }

        var voorstel = geladen.Value;
        var pitNaam = command.PitNaam ?? string.Empty;

        if (voorstel.Spelers.All(s => s.Naam != command.Naam) || (pitNaam.Length > 0 && voorstel.Bestand.Pitten.All(p => p.Naam != pitNaam)))
        {
            return Result.WithMessages(ValidationMessage.Error("Die naam staat niet in het Excel-bestand."));
        }

        var nieuw = ImportPlanner.Herkoppel(voorstel, command.Naam, pitNaam, await avonden.AllesAsync(cancellationToken), await instellingen.LadenAsync(cancellationToken));
        await voorstellen.OpslaanAsync(nieuw, cancellationToken);
        return Result.OK;
    }
}

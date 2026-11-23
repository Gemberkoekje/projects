namespace Klaverjas.application.Commands;

/// <summary>
/// Writes a planned import: the new players, every evening of the workbook (an evening already in the
/// app is replaced and kept as a version, D8) and the evenings without play.
/// </summary>
public sealed class ImportUitvoerenHandler(
    IImportVoorstelStore voorstellen,
    ISpelerStore spelers,
    IAvondStore avonden,
    IInstellingenStore instellingen,
    ILogger<ImportUitvoerenHandler> logger)
{
    private readonly IImportVoorstelStore voorstellen = voorstellen ?? throw new ArgumentNullException(nameof(voorstellen));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly ILogger<ImportUitvoerenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The plan.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What was written, or an error when the plan is gone.</returns>
    public Task<Result<ImportUitgevoerd>> HandleAsync(ImportUitvoeren command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return UitvoerenAsync(command, cancellationToken);
    }

    private async Task<Result<ImportUitgevoerd>> UitvoerenAsync(ImportUitvoeren command, CancellationToken cancellationToken)
    {
        var geladen = await voorstellen.LadenAsync(command.Id, cancellationToken);
        if (!geladen.IsValid)
        {
            return Result.WithMessages<ImportUitgevoerd>(geladen.Messages);
        }

        var voorstel = geladen.Value;
        var oud = await instellingen.LadenAsync(cancellationToken);
        var resultaat = ImportPlanner.Toepassen(voorstel, oud);

        foreach (var speler in resultaat.NieuweSpelers)
        {
            await spelers.OpslaanAsync(speler, cancellationToken);
        }

        foreach (var avond in resultaat.Avonden)
        {
            await avonden.OpslaanAsync(avond, cancellationToken);
        }

        await instellingen.OpslaanAsync(resultaat.Instellingen, cancellationToken);
        await voorstellen.VerwijderenAsync(voorstel.Id, cancellationToken);

        var uitgevoerd = new ImportUitgevoerd
        {
            NieuweSpelers = resultaat.NieuweSpelers.Count,
            Avonden = resultaat.Avonden.Count,
            GeenSpeelavonden = resultaat.Instellingen.GeenSpeelavonden.Count - oud.GeenSpeelavonden.Count,
        };

        logger.LogInformation(
            "Excel {Bestand} ingelezen: {Spelers} nieuwe spelers, {Avonden} avonden, {Vrij} avonden zonder klaverjassen.",
            voorstel.Bestandsnaam,
            uitgevoerd.NieuweSpelers,
            uitgevoerd.Avonden,
            uitgevoerd.GeenSpeelavonden);
        return Result.For(uitgevoerd);
    }
}

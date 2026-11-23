namespace Klaverjas.application.Commands;

/// <summary>Reads the workbook, plans it against what the app already holds, and keeps the plan.</summary>
public sealed class ImportVoorbereidenHandler(
    IStandenLezer lezer,
    ISpelerStore spelers,
    IAvondStore avonden,
    IInstellingenStore instellingen,
    IImportVoorstelStore voorstellen,
    ILogger<ImportVoorbereidenHandler> logger)
{
    private readonly IStandenLezer lezer = lezer ?? throw new ArgumentNullException(nameof(lezer));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IImportVoorstelStore voorstellen = voorstellen ?? throw new ArgumentNullException(nameof(voorstellen));
    private readonly ILogger<ImportVoorbereidenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The file.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The plan's identity, or why the file could not be read.</returns>
    public Task<Result<Guid>> HandleAsync(ImportVoorbereiden command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return VoorbereidenAsync(command, cancellationToken);
    }

    private async Task<Result<Guid>> VoorbereidenAsync(ImportVoorbereiden command, CancellationToken cancellationToken)
    {
        if (command.Inhoud.Count == 0)
        {
            return Result.WithMessages<Guid>(ValidationMessage.Error("Kies eerst het Excel-bestand met de standen."));
        }

        var gelezen = lezer.Lees([.. command.Inhoud]);
        if (!gelezen.IsValid)
        {
            logger.LogWarning("Excel {Bestand} niet te lezen.", command.Bestandsnaam);
            return Result.WithMessages<Guid>(gelezen.Messages);
        }

        var voorstel = ImportPlanner.Maak(
            gelezen.Value,
            await spelers.AllesAsync(cancellationToken),
            await avonden.AllesAsync(cancellationToken),
            await instellingen.LadenAsync(cancellationToken),
            command.Bestandsnaam,
            Guid.NewGuid(),
            Clock.UtcNow());

        await voorstellen.OpslaanAsync(voorstel, cancellationToken);

        logger.LogInformation(
            "Excel {Bestand} gelezen: {Spelers} spelers, {Avonden} avonden, {Verschillen} verschillen.",
            command.Bestandsnaam,
            voorstel.Spelers.Count,
            voorstel.Bestand.Avonden.Count,
            voorstel.Verschillen.Count);
        return Result.For(voorstel.Id);
    }
}

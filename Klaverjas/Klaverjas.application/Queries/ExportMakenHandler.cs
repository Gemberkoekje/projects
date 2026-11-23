using System.Globalization;

namespace Klaverjas.application.Queries;

/// <summary>Collects every player, evening and the latest standings, and has them written to a file.</summary>
public sealed class ExportMakenHandler(
    ISpelerStore spelers,
    IAvondStore avonden,
    IInstellingenStore instellingen,
    IExportSchrijver schrijver,
    ILogger<ExportMakenHandler> logger)
{
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IExportSchrijver schrijver = schrijver ?? throw new ArgumentNullException(nameof(schrijver));
    private readonly ILogger<ExportMakenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The file.</returns>
    public Task<Result<Export>> HandleAsync(ExportMaken query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return MakenAsync(cancellationToken);
    }

    private async Task<Result<Export>> MakenAsync(CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var alleSpelers = await spelers.AllesAsync(cancellationToken);
        var alleAvonden = await avonden.AllesAsync(cancellationToken);
        var vandaag = Kalender.Vandaag();
        var jaar = alleAvonden.Count == 0 ? vandaag.Year : alleAvonden.Max(a => a.Datum).Year;

        var inhoud = schrijver.Schrijf(new ExportGegevens
        {
            Instellingen = instelling,
            Spelers = alleSpelers,
            Avonden = alleAvonden,
            Stand = StandBerekening.Bereken(alleSpelers, alleAvonden, instelling, jaar),
        });

        logger.LogInformation("Export gemaakt: {Spelers} spelers, {Avonden} avonden.", alleSpelers.Count, alleAvonden.Count);
        return Result.For(new Export
        {
            Bestandsnaam = $"Klaverjassen {instelling.Club} {vandaag.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.xlsx",
            Inhoud = inhoud,
        });
    }
}

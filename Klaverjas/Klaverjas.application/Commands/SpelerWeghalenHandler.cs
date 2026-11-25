namespace Klaverjas.application.Commands;

/// <summary>
/// Removes a substitute and their results from every evening (each changed evening keeps its previous
/// version, as any save does). Members are never removed: they stop, and stay in old weeks (D16).
/// </summary>
public sealed class SpelerWeghalenHandler(ISpelerStore spelers, IAvondStore avonden, ILogger<SpelerWeghalenHandler> logger)
{
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ILogger<SpelerWeghalenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">Who.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>How many evenings changed, or why nothing was removed.</returns>
    public Task<Result<int>> HandleAsync(SpelerWeghalen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return WeghalenAsync(command, cancellationToken);
    }

    private async Task<Result<int>> WeghalenAsync(SpelerWeghalen command, CancellationToken cancellationToken)
    {
        var gevonden = await spelers.LadenAsync(command.Id, cancellationToken);
        if (!gevonden.IsValid)
        {
            return Result.WithMessages<int>(gevonden.Messages);
        }

        var speler = gevonden.Value;
        if (speler.IsLid)
        {
            return Result.WithMessages<int>(ValidationMessage.Error($"{speler.Naam} is een lid. Een lid haalt u niet weg: zet het op Gestopt, dan blijven de oude weken kloppen."));
        }

        var gewijzigd = 0;
        foreach (var avond in (await avonden.AllesAsync(cancellationToken)).Where(a => a.Uitslagen.Any(u => u.SpelerId == speler.Id)))
        {
            await avonden.OpslaanAsync(Avond.Van(avond.Datum, avond.Uitslagen.Where(u => u.SpelerId != speler.Id)), cancellationToken);
            gewijzigd++;
        }

        await spelers.WeghalenAsync(speler.Id, cancellationToken);

        logger.LogInformation("Invaller {SpelerId} weggehaald, uit {Avonden} avonden.", speler.Id, gewijzigd);
        return Result.For(gewijzigd);
    }
}

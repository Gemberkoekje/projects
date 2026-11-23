namespace Klaverjas.application.Queries;

/// <summary>Computes the standings of the latest entered evening's season.</summary>
public sealed class StandOphalenHandler(IAvondStore avonden, ISpelerStore spelers, IInstellingenStore instellingen)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The standings and the settings to draw them with.</returns>
    public Task<Result<StandOverzicht>> HandleAsync(StandOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<StandOverzicht>> OphalenAsync(CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var alle = await avonden.AllesAsync(cancellationToken);
        var jaar = alle.Count == 0 ? Kalender.Vandaag().Year : alle.Max(a => a.Datum).Year;

        var stand = StandBerekening.Bereken(await spelers.AllesAsync(cancellationToken), alle, instelling, jaar);

        return Result.For(new StandOverzicht { Stand = stand, Instellingen = instelling });
    }
}

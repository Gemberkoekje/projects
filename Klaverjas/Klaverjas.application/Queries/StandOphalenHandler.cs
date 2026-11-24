using Klaverjas.application.Online;

namespace Klaverjas.application.Queries;

/// <summary>Computes the standings of the latest entered evening's season and compares them with what is online.</summary>
public sealed class StandOphalenHandler(IAvondStore avonden, ISpelerStore spelers, IInstellingenStore instellingen, IOnlineStandStore online)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IOnlineStandStore online = online ?? throw new ArgumentNullException(nameof(online));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The standings, the settings to draw them with, and what is online.</returns>
    public Task<Result<StandOverzicht>> HandleAsync(StandOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<StandOverzicht>> OphalenAsync(CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var weergave = HuidigeStand.Bereken(await spelers.AllesAsync(cancellationToken), await avonden.AllesAsync(cancellationToken), instelling);
        var nuOnline = await online.LadenAsync(cancellationToken);

        return Result.For(new StandOverzicht
        {
            Weergave = weergave,
            Instellingen = instelling,
            Online = nuOnline,
            IsGewijzigd = HuidigeStand.IsGewijzigd(nuOnline, weergave),
        });
    }
}

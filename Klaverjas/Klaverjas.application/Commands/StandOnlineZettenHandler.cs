using Klaverjas.application.Online;

namespace Klaverjas.application.Commands;

/// <summary>
/// Freezes the standings as they are now, with Dad's note, as what the players' page shows. A later
/// correction reaches the players only when he does this again ("Verbeterde stand online zetten").
/// </summary>
public sealed class StandOnlineZettenHandler(
    IAvondStore avonden,
    ISpelerStore spelers,
    IInstellingenStore instellingen,
    IOnlineStandStore online,
    ILogger<StandOnlineZettenHandler> logger)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly ISpelerStore spelers = spelers ?? throw new ArgumentNullException(nameof(spelers));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));
    private readonly IOnlineStandStore online = online ?? throw new ArgumentNullException(nameof(online));
    private readonly ILogger<StandOnlineZettenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The note.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or why not.</returns>
    public Task<Result> HandleAsync(StandOnlineZetten command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return OnlineZettenAsync(command, cancellationToken);
    }

    private async Task<Result> OnlineZettenAsync(StandOnlineZetten command, CancellationToken cancellationToken)
    {
        var bericht = (command.Bericht ?? string.Empty).Trim();

        if (bericht.Length > OnlineStand.MaxBericht)
        {
            return Result.WithMessages(ValidationMessage.Error($"Het bericht is te lang: maak het korter dan {OnlineStand.MaxBericht} tekens."));
        }

        var instelling = await instellingen.LadenAsync(cancellationToken);
        var weergave = HuidigeStand.Bereken(await spelers.AllesAsync(cancellationToken), await avonden.AllesAsync(cancellationToken), instelling);

        if (weergave.IsLeeg)
        {
            return Result.WithMessages(ValidationMessage.Error("Er is nog geen avond ingevuld, dus nog geen stand om online te zetten."));
        }

        await online.OpslaanAsync(
            new OnlineStand
            {
                Stand = weergave,
                Bericht = bericht,
                OnlineSinds = Clock.UtcNow(),
                Vingerafdruk = HuidigeStand.Vingerafdruk(weergave),
            },
            cancellationToken);

        logger.LogInformation("Stand na {Datum} online gezet.", weergave.LaatsteAvond);
        return Result.OK;
    }
}

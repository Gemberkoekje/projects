namespace Kluskat.application.Commands;

/// <summary>Takes back the latest "Gedaan" of a klus, and its entry in the completion log.</summary>
public sealed class GedaanTerugdraaienHandler(
    IKlusRepository repository,
    IVoltooiingStore voltooiingen,
    ILogger<GedaanTerugdraaienHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));
    private readonly ILogger<GedaanTerugdraaienHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(GedaanTerugdraaien command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return TerugdraaienAsync(command, cancellationToken);
    }

    private async Task<Result> TerugdraaienAsync(GedaanTerugdraaien command, CancellationToken cancellationToken)
    {
        var result = await KlusMutatie.UitvoerenAsync(repository, command.KlusId, klus => klus.Terugdraaien(), cancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Klus {KlusId} terugdraaien afgewezen: {Reden}", command.KlusId, result.Redenen());
            return result;
        }

        await voltooiingen.LaatsteVerwijderenAsync(command.KlusId, cancellationToken);

        logger.LogInformation("Gedaan van klus {KlusId} teruggedraaid.", command.KlusId);
        return Result.OK;
    }
}

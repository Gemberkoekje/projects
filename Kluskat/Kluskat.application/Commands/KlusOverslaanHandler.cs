namespace Kluskat.application.Commands;

/// <summary>Records a skip on the klus's stream and hides it for the rest of the skip window.</summary>
public sealed class KlusOverslaanHandler(
    IKlusRepository repository,
    ISessieStore sessies,
    ILogger<KlusOverslaanHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ISessieStore sessies = sessies ?? throw new ArgumentNullException(nameof(sessies));
    private readonly ILogger<KlusOverslaanHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus and the reason.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(KlusOverslaan command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return OverslaanAsync(command, cancellationToken);
    }

    private async Task<Result> OverslaanAsync(KlusOverslaan command, CancellationToken cancellationToken)
    {
        var nu = Clock.UtcNow();
        var result = await KlusMutatie.UitvoerenAsync(repository, command.KlusId, klus => klus.Overslaan(command.Reden), cancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Klus {KlusId} overslaan afgewezen: {Reden}", command.KlusId, result.Redenen());
            return result;
        }

        var sessie = (await sessies.LadenAsync(cancellationToken)).Actueel(nu);
        await sessies.OpslaanAsync(sessie.NaOverslaan(KlusOverzicht.Van(result.Value), command.Reden, nu), cancellationToken);

        logger.LogInformation("Klus {KlusId} overgeslagen: {OverslaanReden}.", command.KlusId, command.Reden);
        return Result.OK;
    }
}

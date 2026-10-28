namespace Kluskat.application.Commands;

/// <summary>Edits the descriptive fields of a klus.</summary>
public sealed class KlusAanpassenHandler(IKlusRepository repository, ILogger<KlusAanpassenHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<KlusAanpassenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus and its new values.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(KlusAanpassen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return AanpassenAsync(command, cancellationToken);
    }

    private async Task<Result> AanpassenAsync(KlusAanpassen command, CancellationToken cancellationToken)
    {
        var result = await KlusMutatie.UitvoerenAsync(repository, command.KlusId, klus => klus.Aanpassen(command.Data), cancellationToken);

        if (result.IsValid)
        {
            logger.LogInformation("Klus {KlusId} aangepast.", command.KlusId);
        }
        else
        {
            logger.LogWarning("Klus {KlusId} aanpassen afgewezen: {Reden}", command.KlusId, result.Redenen());
        }

        return result;
    }
}

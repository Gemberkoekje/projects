namespace Kluskat.application.Commands;

/// <summary>Takes a klus off the list for good. Its history stays.</summary>
public sealed class KlusSchrappenHandler(IKlusRepository repository, ILogger<KlusSchrappenHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<KlusSchrappenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(KlusSchrappen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return SchrappenAsync(command, cancellationToken);
    }

    private async Task<Result> SchrappenAsync(KlusSchrappen command, CancellationToken cancellationToken)
    {
        var result = await KlusMutatie.UitvoerenAsync(repository, command.KlusId, klus => klus.Schrappen(), cancellationToken);

        if (result.IsValid)
        {
            logger.LogInformation("Klus {KlusId} geschrapt.", command.KlusId);
        }
        else
        {
            logger.LogWarning("Klus {KlusId} schrappen afgewezen: {Reden}", command.KlusId, result.Redenen());
        }

        return result;
    }
}

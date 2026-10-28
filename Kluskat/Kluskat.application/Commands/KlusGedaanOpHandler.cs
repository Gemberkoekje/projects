namespace Kluskat.application.Commands;

/// <summary>
/// Records a completion entered afterwards. It moves the klus, but it is not logged as a
/// <see cref="Voltooiing"/>: it was not ticked off on the klus screen.
/// </summary>
public sealed class KlusGedaanOpHandler(IKlusRepository repository, ILogger<KlusGedaanOpHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<KlusGedaanOpHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The klus and the date.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages that stopped it.</returns>
    public Task<Result> HandleAsync(KlusGedaanOp command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return GedaanOpAsync(command, cancellationToken);
    }

    private async Task<Result> GedaanOpAsync(KlusGedaanOp command, CancellationToken cancellationToken)
    {
        // Only a date is known, so noon stands in for the time. Today's noon may still be ahead.
        var nu = Clock.UtcNow();
        var middag = Kalender.Middag(command.Datum);
        var op = middag > nu ? nu : middag;

        var result = await KlusMutatie.UitvoerenAsync(repository, command.KlusId, klus => klus.Gedaan(op, viaBeheer: true), cancellationToken);

        if (result.IsValid)
        {
            logger.LogInformation("Klus {KlusId} gedaan op {Datum} ingevuld.", command.KlusId, command.Datum);
        }
        else
        {
            logger.LogWarning("Klus {KlusId} gedaan op {Datum} afgewezen: {Reden}", command.KlusId, command.Datum, result.Redenen());
        }

        return result;
    }
}

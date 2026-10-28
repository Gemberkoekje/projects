namespace Kluskat.application.Commands;

/// <summary>Adds a klus to the list.</summary>
public sealed class KlusToevoegenHandler(IKlusRepository repository, ILogger<KlusToevoegenHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<KlusToevoegenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The new klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the validation messages.</returns>
    public Task<Result> HandleAsync(KlusToevoegen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToevoegenAsync(command, cancellationToken);
    }

    private async Task<Result> ToevoegenAsync(KlusToevoegen command, CancellationToken cancellationToken)
    {
        var laatstGedaan = command.LaatstGedaanOp == default
            ? Kalender.Middernacht(Kalender.Datum(Clock.UtcNow())).AddDays(-command.Data.IntervalDagen)
            : Kalender.Middag(command.LaatstGedaanOp);

        var klus = Klus.Toevoegen(command.Data, laatstGedaan);
        if (!klus.IsValid)
        {
            return klus;
        }

        var opgeslagen = await repository.OpslaanAsync(klus.Value, cancellationToken);

        if (opgeslagen.IsValid)
        {
            logger.LogInformation("Klus {KlusId} ({Ruimte}: {Naam}) toegevoegd.", klus.Value.Id, klus.Value.Ruimte, klus.Value.Naam);
        }

        return opgeslagen;
    }
}

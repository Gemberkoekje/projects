using Kluskat.application.Startvulling;

namespace Kluskat.application.Commands;

/// <summary>Seeds an empty list, staggered. A list that ever held a klus is left alone.</summary>
public sealed class StartvullingToepassenHandler(
    IKlusRepository repository,
    IKlusOverzichtStore overzichten,
    ILogger<StartvullingToepassenHandler> logger)
{
    private readonly IKlusRepository repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly ILogger<StartvullingToepassenHandler> logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>Handles the command.</summary>
    /// <param name="command">The seed list.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>OK, or the messages of the klussen that could not be seeded.</returns>
    public Task<Result> HandleAsync(StartvullingToepassen command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ToepassenAsync(command, cancellationToken);
    }

    private async Task<Result> ToepassenAsync(StartvullingToepassen command, CancellationToken cancellationToken)
    {
        // Checked against every klus ever stored, not just active ones: schrappen the whole list on
        // the beheer page must not make the seed come back on the next deploy.
        if (await overzichten.HeeftKlussenAsync(cancellationToken))
        {
            logger.LogDebug("Startvulling overgeslagen: er zijn al klussen.");
            return Result.OK;
        }

        var laatstGedaan = Spreiding.LaatstGedaan([.. command.Klussen.Select(k => k.IntervalDagen)], Clock.UtcNow());
        var redenen = new List<IValidationMessage>();
        var gelukt = 0;

        for (var i = 0; i < command.Klussen.Count; i++)
        {
            var klus = Klus.Toevoegen(command.Klussen[i], laatstGedaan[i]);
            var opgeslagen = klus.IsValid ? await repository.OpslaanAsync(klus.Value, cancellationToken) : klus;

            if (opgeslagen.IsValid)
            {
                gelukt++;
            }
            else
            {
                logger.LogWarning("Startvulling: {Naam} niet toegevoegd: {Reden}", command.Klussen[i].Naam, opgeslagen.Redenen());
                redenen.AddRange(opgeslagen.Messages);
            }
        }

        logger.LogInformation("Startvulling: {Aantal} klussen toegevoegd.", gelukt);
        return redenen.Count == 0 ? Result.OK : Result.WithMessages(redenen);
    }
}

namespace Kluskat.application.Queries;

/// <summary>
/// Gathers the win screen: the klus, and what its latest completion brought — scored as the log
/// with and without it, so a refresh of the page celebrates the same thing again.
/// </summary>
public sealed class OverwinningOphalenHandler(IKlusOverzichtStore overzichten, IVoltooiingStore voltooiingen, IProfielStore profielen)
{
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));
    private readonly IProfielStore profielen = profielen ?? throw new ArgumentNullException(nameof(profielen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The win, or a message when the klus or its completion is unknown.</returns>
    public Task<Result<Overwinning>> HandleAsync(OverwinningOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(query, cancellationToken);
    }

    private async Task<Result<Overwinning>> OphalenAsync(OverwinningOphalen query, CancellationToken cancellationToken)
    {
        var klus = await overzichten.LadenAsync(query.KlusId, cancellationToken);
        if (!klus.IsValid)
        {
            return Result.WithMessages<Overwinning>(klus.Messages);
        }

        var gegevens = await Spelgegevens.LadenAsync(voltooiingen, overzichten, profielen, cancellationToken);
        var deze = gegevens.Log
            .Where(v => v.KlusId == klus.Value.Id)
            .OrderByDescending(v => v.Op)
            .FirstOrDefault();

        if (deze is null)
        {
            return Result.WithMessages<Overwinning>(ValidationMessage.Error("Deze klus is nog niet gedaan."));
        }

        var beloning = Spelregels.Beloning(gegevens.Log, deze, gegevens.Ruimtes, Clock.UtcNow());

        return Result.For(new Overwinning
        {
            Klus = klus.Value,
            Beloning = beloning,
            Accessoire = gegevens.Draagt(beloning.Na),
        });
    }
}

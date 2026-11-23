namespace Klaverjas.application.Queries;

/// <summary>Lists the season's evenings, newest first, with the evenings without play among them.</summary>
public sealed class AvondenOphalenHandler(IAvondStore avonden, IInstellingenStore instellingen)
{
    private readonly IAvondStore avonden = avonden ?? throw new ArgumentNullException(nameof(avonden));
    private readonly IInstellingenStore instellingen = instellingen ?? throw new ArgumentNullException(nameof(instellingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The overview.</returns>
    public Task<Result<AvondenOverzicht>> HandleAsync(AvondenOphalen query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return OphalenAsync(cancellationToken);
    }

    private async Task<Result<AvondenOverzicht>> OphalenAsync(CancellationToken cancellationToken)
    {
        var instelling = await instellingen.LadenAsync(cancellationToken);
        var alle = await avonden.AllesAsync(cancellationToken);
        var volgende = Kalender.VolgendeAvond(instelling, alle.Select(a => a.Datum));

        // The season shown is the latest entered evening's; before the first one, the next evening's.
        var jaar = alle.Count == 0 ? volgende.Year : alle.Max(a => a.Datum).Year;

        var gespeeld = alle
            .Where(a => a.Datum.Year == jaar)
            .Select(a => new AvondRegel
            {
                Datum = a.Datum,
                Gespeeld = a.Uitslagen.Count(u => u.Status is Status.Speelt or Status.Slaper),
                Slapers = a.Uitslagen.Count(u => u.Status == Status.Slaper),
            });

        var vrij = instelling.GeenSpeelavonden
            .Where(g => g.Datum.Year == jaar && g.Datum < volgende)
            .Select(g => new AvondRegel { Datum = g.Datum, GeenSpeelavond = g.Reden });

        return Result.For(new AvondenOverzicht
        {
            VolgendeAvond = volgende,
            Regels = [.. gespeeld.Concat(vrij).OrderByDescending(r => r.Datum)],
        });
    }
}

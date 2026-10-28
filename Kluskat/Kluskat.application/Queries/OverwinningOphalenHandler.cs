namespace Kluskat.application.Queries;

/// <summary>Gathers what the win screen shows: the klus, and how many were done this week.</summary>
public sealed class OverwinningOphalenHandler(IKlusOverzichtStore overzichten, IVoltooiingStore voltooiingen)
{
    private readonly IKlusOverzichtStore overzichten = overzichten ?? throw new ArgumentNullException(nameof(overzichten));
    private readonly IVoltooiingStore voltooiingen = voltooiingen ?? throw new ArgumentNullException(nameof(voltooiingen));

    /// <summary>Handles the query.</summary>
    /// <param name="query">The klus.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The win, or a message when the klus does not exist.</returns>
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

        var aantal = await voltooiingen.AantalSindsAsync(Kalender.BeginVanDeWeek(Clock.UtcNow()), cancellationToken);

        return Result.For(new Overwinning { Klus = klus.Value, AantalDezeWeek = aantal });
    }
}
